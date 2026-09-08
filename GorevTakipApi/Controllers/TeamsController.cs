using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GorevTakipApi.Data;
using GorevTakipApi.Extensions;
using GorevTakipApi.Models;

namespace GorevTakipApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TeamsController : ControllerBase
{
    private readonly AppDbContext _context;

    public TeamsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Team>>> GetTeams()
    {
        if (User.IsAdmin())
            return await _context.Teams.ToListAsync();

        var teamId = User.GetTeamId();
        if (teamId == null) return Ok(Enumerable.Empty<Team>());

        return await _context.Teams.Where(t => t.Id == teamId).ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Team>> GetTeam(int id)
    {
        if (!User.IsAdmin() && id != User.GetTeamId())
            return Forbid();

        var team = await _context.Teams.FindAsync(id);
        if (team == null) return NotFound();
        return team;
    }

    // Çıplak Team entity'si bağlamıyoruz: EF'in Add() metodu navigasyon alanları üzerinden
    // ulaşabildiği nesneleri de kaydettiği için, gövdeye iç içe bir "users" dizisi koyup
    // doğrudan veritabanına Admin rolünde kullanıcı yazmak mümkündü (mass assignment).
    public record TeamRequest(string Name);

    private static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Ekip adı zorunludur.";
        if (name.Trim().Length > 150) return "Ekip adı en fazla 150 karakter olabilir.";
        return null;
    }

    [HttpPost]
    public async Task<ActionResult<Team>> CreateTeam(TeamRequest request)
    {
        // Ekip oluşturma yalnızca yöneticide olsun; aksi halde herkes kendine ekip açıp
        // izolasyonun dışına çıkabilir.
        if (!User.IsAdmin())
            return Forbid();

        var error = ValidateName(request.Name);
        if (error != null) return BadRequest(error);

        var team = new Team { Name = request.Name.Trim(), CreatedAt = DateTime.UtcNow };

        _context.Teams.Add(team);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetTeam), new { id = team.Id }, team);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTeam(int id, TeamRequest request)
    {
        // Ekip adını değiştirmek yönetsel bir işlem. Önceden yalnızca "bu ekibin üyesi mi"
        // diye bakılıyordu, yani sıradan bir ekip üyesi de ekibin adını değiştirebiliyordu.
        var isOwnTeamLeader = User.GetRole() == Role.TeamLeader && id == User.GetTeamId();
        if (!User.IsAdmin() && !isOwnTeamLeader)
            return Forbid();

        var error = ValidateName(request.Name);
        if (error != null) return BadRequest(error);

        // Takip edilen entity üzerinden sadece Name alanını güncelliyoruz; client'ın
        // göndermediği CreatedAt gibi denetim alanları EntityState.Modified ile ezilmesin.
        var existing = await _context.Teams.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Name = request.Name.Trim();
        existing.ModifiedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Ekibi siler. Ekibe bağlı projeler varsa işlem reddediliyor - projeler silinseydi
    /// görevleri ve yorumları da zincirleme yok olurdu. Üyeler silinmez, ekipsiz kalır.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTeam(int id)
    {
        // Ekip silmek yıkıcı bir işlem (üyeleri/projeleri etkiler), sadece yönetici yapabilsin.
        if (!User.IsAdmin())
            return Forbid();

        var team = await _context.Teams.FindAsync(id);
        if (team == null) return NotFound();

        var projectCount = await _context.Projects.CountAsync(p => p.TeamId == id);
        if (projectCount > 0)
            return BadRequest($"Bu ekibin {projectCount} projesi var. Ekibi silmeden önce projeleri başka bir ekibe taşıyın veya silin.");

        // Üyeleri ekipsiz bırakıyoruz. Önceden bu yapılmadığı için üyesi olan bir ekip
        // foreign key ihlaline takılıyor ve hiçbir zaman silinemiyordu.
        //
        // ExecuteUpdateAsync kendi başına anında commit ettiği için ikisini tek transaction'a
        // alıyoruz: aksi halde silme adımı patlarsa ekip duruyor ama bütün üyeleri ekipsiz
        // kalmış oluyordu.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.Users
            .Where(u => u.TeamId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TeamId, (int?)null));

        _context.Teams.Remove(team);
        await _context.SaveChangesAsync();

        await transaction.CommitAsync();
        return NoContent();
    }
}
