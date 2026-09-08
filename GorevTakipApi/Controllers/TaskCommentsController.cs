using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GorevTakipApi.Data;
using GorevTakipApi.Extensions;
using GorevTakipApi.Models;
using GorevTakipApi.Services;

namespace GorevTakipApi.Controllers;

public class AddCommentRequest
{
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Bir görevin yorumları. Görevi görebilen herkes (görev zaten ekip izolasyonuyla
/// korunuyor) yorum okuyup yazabilir - görev yönetimi (oluşturma/silme/atama) yetkisi
/// aranmıyor, çünkü Jira/Azure DevOps'ta da herhangi bir ekip üyesi work item'a yorum
/// yazabiliyor.
/// </summary>
[ApiController]
[Route("api/tasks/{taskId}/comments")]
[Authorize]
public class TaskCommentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ITaskActivityService _activityService;

    public TaskCommentsController(AppDbContext context, INotificationService notificationService, ITaskActivityService activityService)
    {
        _context = context;
        _notificationService = notificationService;
        _activityService = activityService;
    }

    private async Task<TaskItem?> GetAccessibleTaskAsync(int taskId)
    {
        var task = await _context.Tasks.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == taskId);
        if (task == null) return null;

        if (!User.IsAdmin() && task.Project?.TeamId != User.GetTeamId())
            return null;

        return task;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetComments(int taskId)
    {
        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();

        // Include gerekmiyor: aşağıdaki Select projeksiyonu zaten gereken JOIN'i üretiyor,
        // Include ise projeksiyonlu sorgularda yok sayılıyordu.
        var comments = await _context.TaskComments
            .AsNoTracking()
            .Where(c => c.TaskItemId == taskId)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.Content,
                c.CreatedAt,
                c.UserId,
                UserFullName = c.User != null ? c.User.FullName : "Silinmiş kullanıcı",
                UserAvatarUrl = c.User != null ? c.User.AvatarUrl : null
            })
            .ToListAsync();

        return Ok(comments);
    }

    [HttpPost]
    public async Task<IActionResult> AddComment(int taskId, AddCommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Yorum boş olamaz.");

        var content = request.Content.Trim();
        if (content.Length > 2000)
            return BadRequest("Yorum en fazla 2000 karakter olabilir.");

        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();

        var userId = User.GetUserId();

        var comment = new TaskComment
        {
            TaskItemId = taskId,
            UserId = userId,
            Content = content,
            CreatedAt = DateTime.UtcNow
        };

        _context.TaskComments.Add(comment);
        _activityService.Log(taskId, userId, "comment", "yorum yazdı");

        // Göreve atanmış kişiye (yorumu yazan kendisi değilse) haber ver. Bildirimi yorumla
        // aynı SaveChanges içinde kaydediyoruz; ayrı kaydedildiğinde bildirim hata verse bile
        // yorum veritabanında kalıyor ve client hata gördüğü için tekrar göndererek yorumu
        // ikinci kez oluşturuyordu.
        if (task.AssignedToId.HasValue && task.AssignedToId.Value != userId)
        {
            _notificationService.Queue(
                task.AssignedToId.Value,
                $"\"{task.Title}\" görevine yeni bir yorum yazıldı.",
                task.Id);
        }

        await _context.SaveChangesAsync();

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        return CreatedAtAction(nameof(GetComments), new { taskId }, new
        {
            comment.Id,
            comment.Content,
            comment.CreatedAt,
            comment.UserId,
            UserFullName = user?.FullName ?? "Bilinmeyen kullanıcı",
            UserAvatarUrl = user?.AvatarUrl
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteComment(int taskId, int id)
    {
        // Diğer uçlarla tutarlı olsun diye önce göreve erişim doğrulanıyor; aksi halde
        // ekipten çıkarılmış bir kullanıcı artık göremediği görevlerdeki yorumlarını
        // silmeye devam edebiliyordu.
        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();

        var comment = await _context.TaskComments.FirstOrDefaultAsync(c => c.Id == id && c.TaskItemId == taskId);
        if (comment == null) return NotFound();

        // Sadece yorumu yazan kişi ya da Admin silebilir.
        if (comment.UserId != User.GetUserId() && !User.IsAdmin())
            return Forbid();

        _context.TaskComments.Remove(comment);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
