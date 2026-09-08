using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GorevTakipApi.Data;
using GorevTakipApi.Extensions;

namespace GorevTakipApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationsController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Sadece giriş yapan kullanıcının kendi bildirimlerini döner - başka bir kullanıcının
    /// bildirimlerini görmesi mümkün değil, ekstra bir izolasyon kontrolüne gerek yok
    /// çünkü sorgu zaten User.GetUserId() ile filtreleniyor.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetMyNotifications()
    {
        var userId = User.GetUserId();

        var notifications = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            // Aynı işlemde üretilen bildirimler aynı CreatedAt değerini paylaşabiliyor.
            // Eşitlik kırıcı olmadan sıralama kararsız kalıyor ve sınırdaki bildirimler
            // sorgudan sorguya kaybolabiliyordu.
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(50)
            .Select(n => new { n.Id, n.Message, n.IsRead, n.CreatedAt, n.TaskItemId })
            .ToListAsync();

        return Ok(notifications);
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var userId = User.GetUserId();
        var notification = await _context.Notifications.FindAsync(id);

        if (notification == null || notification.UserId != userId)
            return NotFound();

        notification.IsRead = true;
        notification.ModifiedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = User.GetUserId();

        // Tüm okunmamış bildirimleri belleğe çekip tek tek işaretlemek yerine tek bir
        // UPDATE cümlesi çalıştırıyoruz; bildirim sayısı arttıkça fark büyüyor.
        await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ModifiedAt, DateTime.UtcNow));

        return NoContent();
    }
}
