using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GorevTakipApi.Data;
using GorevTakipApi.Extensions;
using GorevTakipApi.Models;
using GorevTakipApi.Services;

namespace GorevTakipApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ITaskActivityService _activityService;
    private readonly IFileStorage _storage;

    public ProjectsController(AppDbContext context, INotificationService notificationService,
        ITaskActivityService activityService, IFileStorage storage)
    {
        _context = context;
        _notificationService = notificationService;
        _activityService = activityService;
        _storage = storage;
    }

    // TasksController'daki IsTaskManager ile aynı kural: proje oluşturma, düzenleme ve
    // silme yalnızca Admin ve Ekip Liderine açık. Önceden burada hiç rol kontrolü yoktu;
    // sıradan bir ekip üyesi kendi ekibinin projesini - dolayısıyla projeye bağlı bütün
    // görev ve yorumları - silebiliyordu.
    private bool IsProjectManager => User.IsAdmin() || User.GetRole() == Role.TeamLeader;

    /// <summary>
    /// Proje yanıtı. Entity yerine projeksiyon dönüyoruz: Project.Tasks koleksiyonu
    /// yüklenmediği için client'a her zaman boş dizi olarak gidiyor ve "projede hiç görev
    /// yok" gibi görünüyordu. Bunun yerine görev sayısını ve tamamlanma oranını hesaplayıp
    /// gönderiyoruz.
    /// </summary>
    public record ProjectDto(
        int Id, string Name, int TeamId, string? TeamName,
        int TaskCount, int CompletedTaskCount, DateTime? DueDate);

    private static IQueryable<ProjectDto> ToDto(IQueryable<Project> query) =>
        query.Select(p => new ProjectDto(
            p.Id, p.Name, p.TeamId, p.Team!.Name,
            p.Tasks.Count(),
            p.Tasks.Count(t => t.Status == "done"),
            p.DueDate));

    public record CreateProjectRequest(string Name, int TeamId, DateTime? DueDate);
    public record UpdateProjectRequest(string Name, int TeamId, DateTime? DueDate);

    private static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Proje adı zorunludur.";
        if (name.Trim().Length > 150) return "Proje adı en fazla 150 karakter olabilir.";
        return null;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectDto>>> GetProjects()
    {
        var query = _context.Projects.AsNoTracking().AsQueryable();

        if (!User.IsAdmin())
        {
            var teamId = User.GetTeamId();
            if (teamId == null) return Ok(Enumerable.Empty<ProjectDto>());
            query = query.Where(p => p.TeamId == teamId);
        }

        return Ok(await ToDto(query).ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ProjectDto>> GetProject(int id)
    {
        var project = await _context.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        if (!User.IsAdmin() && project.TeamId != User.GetTeamId())
            return Forbid();

        var dto = await ToDto(_context.Projects.AsNoTracking().Where(p => p.Id == id)).FirstAsync();
        return Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> CreateProject(CreateProjectRequest request)
    {
        if (!IsProjectManager) return Forbid();

        var error = ValidateName(request.Name);
        if (error != null) return BadRequest(error);

        int teamId;
        if (User.IsAdmin())
        {
            teamId = request.TeamId;
        }
        else
        {
            var myTeamId = User.GetTeamId();
            if (myTeamId == null)
                return BadRequest("Bir ekibe atanmamış kullanıcı proje oluşturamaz.");
            // Client'ın gönderdiği TeamId'ye güvenme; her zaman kullanıcının kendi ekibini ata.
            teamId = myTeamId.Value;
        }

        if (!await _context.Teams.AnyAsync(t => t.Id == teamId))
            return BadRequest("Geçersiz ekip.");

        var project = new Project
        {
            Name = request.Name.Trim(),
            TeamId = teamId,
            // Client saat dilimi olmayan bir tarih gönderiyor; Npgsql yalnızca
            // Kind=Utc kabul ettiği için görevlerdeki ile aynı dönüşümden geçiyor.
            DueDate = request.DueDate.ToUtcSafe(),
            CreatedAt = DateTime.UtcNow
        };

        _context.Projects.Add(project);

        // Ekibin tamamı haberdar olsun; projeyi açan kişi kendi işlemi için
        // bildirim almıyor.
        var dueText = project.DueDate == null
            ? string.Empty
            : $" Bitiş tarihi: {project.DueDate.Value:dd.MM.yyyy}.";

        await _notificationService.QueueTeamAsync(
            teamId,
            $"\"{project.Name}\" adlı yeni bir proje ekibinize eklendi.{dueText}",
            excludeUserId: User.GetUserId());

        await _context.SaveChangesAsync();

        var dto = await ToDto(_context.Projects.AsNoTracking().Where(p => p.Id == project.Id)).FirstAsync();
        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, dto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProject(int id, UpdateProjectRequest request)
    {
        if (!IsProjectManager) return Forbid();

        var error = ValidateName(request.Name);
        if (error != null) return BadRequest(error);

        // Takip edilen entity üzerinde sadece izin verilen alanları güncelliyoruz.
        // Önceden client'tan gelen nesne EntityState.Modified yapılıyordu; bu, gönderilmeyen
        // CreatedAt/CreatedBy gibi denetim alanlarını boş değerlerle eziyordu.
        var existing = await _context.Projects.FindAsync(id);
        if (existing == null) return NotFound();

        if (!User.IsAdmin() && existing.TeamId != User.GetTeamId())
            return Forbid();

        var previousTeamId = existing.TeamId;

        // Ekip değişikliğine yalnızca Admin yetkili; aksi halde ekip lideri projeyi başka
        // bir ekibe taşıyarak ekip izolasyonunu atlatabilirdi.
        var newTeamId = User.IsAdmin() ? request.TeamId : existing.TeamId;

        if (newTeamId != previousTeamId && !await _context.Teams.AnyAsync(t => t.Id == newTeamId))
            return BadRequest("Geçersiz ekip.");

        existing.Name = request.Name.Trim();
        existing.TeamId = newTeamId;
        existing.DueDate = request.DueDate.ToUtcSafe();
        existing.ModifiedAt = DateTime.UtcNow;

        if (newTeamId == previousTeamId)
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }

        await _notificationService.QueueTeamAsync(
            newTeamId,
            $"\"{existing.Name}\" adlı proje ekibinize atandı.",
            excludeUserId: User.GetUserId());

        // Proje ekip değiştirdiğinde, görevlerdeki eski ekip üyeleri artık o görevleri
        // listeleyemiyor ama atama kayıtlarda duruyordu: iş "birine atanmış" görünürken
        // atanan kişi görevi hiç göremediği için sessizce askıda kalıyordu. Yeni ekipte de
        // olan kişilerin ataması korunuyor, diğerleri kaldırılıyor.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.SaveChangesAsync();

        // AssignedToId != null şartı kritik: AssignedTo isteğe bağlı bir navigasyon olduğu
        // için EF LEFT JOIN üretiyor ve bu şart olmadan ATANMAMIŞ her görev de
        // "TeamId IS NULL" üzerinden eşleşip boş bir geçmiş kaydı yaratıyordu.
        //
        // Admin'ler hariç: ValidateAssigneeAsync bir Admin'in her ekibin görevine
        // atanmasına bilerek izin veriyor ve Admin zaten bütün görevleri görebiliyor.
        var orphanedQuery = _context.Tasks.Where(t =>
            t.ProjectId == id &&
            t.AssignedToId != null &&
            t.AssignedTo!.Role != Role.Admin &&
            t.AssignedTo!.TeamId != newTeamId);

        var orphaned = await orphanedQuery
            .Select(t => new { t.Id, AssigneeName = t.AssignedTo!.FullName })
            .ToListAsync();

        if (orphaned.Count > 0)
        {
            // Güncellemeyi yukarıdaki sorgu üzerinden değil, elimizdeki kimlik listesiyle
            // yapıyoruz: yukarıdaki sorgu Users tablosuna JOIN gerektiriyor ve JOIN'li bir
            // toplu UPDATE her sağlayıcıda güvenilir biçimde çevrilmiyor. Kimlik listesiyle
            // çalışan hali düz bir "WHERE Id IN (...)" üretiyor.
            var orphanedIds = orphaned.Select(x => x.Id).ToList();

            await _context.Tasks
                .Where(t => orphanedIds.Contains(t.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AssignedToId, (int?)null));

            var actorId = User.GetUserId();
            foreach (var item in orphaned)
            {
                _activityService.Log(item.Id, actorId, "assigned",
                    $"projeyi başka bir ekibe taşıdığı için {item.AssigneeName} üzerindeki atamayı kaldırdı");
            }

            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return NoContent();
    }

    /// <summary>
    /// Projeyi siler. Projeye bağlı görevler ve o görevlerin yorumları veritabanı
    /// seviyesinde zincirleme siliniyor; bu yıkıcı bir işlem olduğu için görevi olan bir
    /// projede açık onay (force=true) istiyoruz. Önceden hiçbir uyarı olmadan siliniyordu.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProject(int id, [FromQuery] bool force = false)
    {
        if (!IsProjectManager) return Forbid();

        var project = await _context.Projects.FindAsync(id);
        if (project == null) return NotFound();

        if (!User.IsAdmin() && project.TeamId != User.GetTeamId())
            return Forbid();

        var taskCount = await _context.Tasks.CountAsync(t => t.ProjectId == id);
        if (taskCount > 0 && !force)
            return BadRequest($"Bu projede {taskCount} görev var. Proje silinirse görevler ve yorumları da silinecek.");

        // Görevler zincirleme silinince ekleri de veritabanından gidiyor ama diskteki
        // dosyalar kalıyordu; App_Data/attachments hiçbir kayda bağlı olmayan dosyalarla
        // dolup büyüyordu. Silmeden önce hangi dosyaların gideceğini not alıyoruz.
        var storedFiles = await _context.TaskAttachments
            .Where(a => a.TaskItem!.ProjectId == id)
            .Select(a => a.StoredFileName)
            .ToListAsync();

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        // Proje silindi; dosya silinemezse yalnızca artık bir dosya kalır.
        foreach (var stored in storedFiles)
            _storage.TryDelete(_storage.AttachmentsFolder, stored);

        return NoContent();
    }
}
