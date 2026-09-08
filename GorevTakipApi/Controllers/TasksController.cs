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
public class TasksController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ITaskActivityService _activityService;
    private readonly IFileStorage _storage;

    public TasksController(
        AppDbContext context,
        INotificationService notificationService,
        ITaskActivityService activityService,
        IFileStorage storage)
    {
        _context = context;
        _notificationService = notificationService;
        _activityService = activityService;
        _storage = storage;
    }

    // Admin ve Ekip Lideri "yönetici" sayılır: görev oluşturabilir, silebilir, başkasına
    // atayabilir. Sıradan Ekip Üyesi bunları yapamaz - sadece kendine atanmış bir görevin
    // durumunu değiştirebilir (board'da kartını sürükleyebilir).
    private bool IsTaskManager => User.IsAdmin() || User.GetRole() == Role.TeamLeader;

    private static readonly string[] ValidStatuses = { "todo", "in_progress", "done" };
    private static readonly string[] ValidPriorities = { "Low", "Medium", "High", "Critical" };

    /// <summary>Tamamlanmış görevler bu kadar gün sonra panoda varsayılan olarak gizlenir.</summary>
    private const int ArchiveAfterDays = 30;

    // ---------------------------------------------------------------- DTO'lar

    public record TaskDto(
        int Id, string Title, string? Description, string Status, string Priority,
        DateTime? DueDate, int SortOrder, DateTime? CompletedAt,
        int ProjectId, string? ProjectName,
        int? AssignedToId, string? AssignedToFullName,
        int CommentCount, int AttachmentCount, int ChecklistTotal, int ChecklistDone);

    public record ChecklistItemDto(int Id, string Text, bool IsDone, int SortOrder);

    public record AttachmentDto(
        int Id, string FileName, string Url, string ContentType, long SizeBytes,
        int? UserId, string? UserFullName, DateTime CreatedAt);

    public record ActivityDto(
        int Id, string ActivityType, string Detail,
        int? UserId, string? UserFullName, string? UserAvatarUrl, DateTime CreatedAt);

    public record TaskDetailDto(
        TaskDto Task,
        IEnumerable<ChecklistItemDto> Checklist,
        IEnumerable<AttachmentDto> Attachments);

    // ---------------------------------------------------------------- Okuma

    /// <summary>
    /// Ekibin görevlerini döner. Tamamlanmış ve üzerinden 30 günden fazla geçmiş görevler
    /// varsayılan olarak gizleniyor; aksi halde "Tamamlandı" sütunu sonsuza kadar büyüyor
    /// ve panoda gerçekten bakılması gereken işleri gölgeliyor.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskDto>>> GetTasks([FromQuery] bool includeArchived = false)
    {
        var query = _context.Tasks.AsNoTracking().AsQueryable();

        if (!User.IsAdmin())
        {
            var teamId = User.GetTeamId();
            if (teamId == null) return Ok(Enumerable.Empty<TaskDto>());
            query = query.Where(t => t.Project!.TeamId == teamId);
        }

        if (!includeArchived)
        {
            var threshold = DateTime.UtcNow.AddDays(-ArchiveAfterDays);
            // CompletedAt'i null olan eski kayıtlar gizlenmesin diye ayrıca kontrol ediyoruz.
            query = query.Where(t => t.Status != "done" || t.CompletedAt == null || t.CompletedAt >= threshold);
        }

        var result = await query
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .Select(t => new TaskDto(
                t.Id, t.Title, t.Description, t.Status, t.Priority, t.DueDate, t.SortOrder, t.CompletedAt,
                t.ProjectId, t.Project!.Name,
                t.AssignedToId, t.AssignedTo != null ? t.AssignedTo.FullName : null,
                _context.TaskComments.Count(c => c.TaskItemId == t.Id),
                t.Attachments.Count(),
                t.ChecklistItems.Count(),
                t.ChecklistItems.Count(c => c.IsDone)))
            .ToListAsync();

        return Ok(result);
    }

    /// <summary>Görevin kendi sayfası için: görev + kontrol listesi + dosya ekleri.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<TaskDetailDto>> GetTask(int id)
    {
        var task = await GetAccessibleTaskAsync(id, tracked: false);
        if (task == null) return NotFound();

        var dto = await _context.Tasks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TaskDto(
                t.Id, t.Title, t.Description, t.Status, t.Priority, t.DueDate, t.SortOrder, t.CompletedAt,
                t.ProjectId, t.Project!.Name,
                t.AssignedToId, t.AssignedTo != null ? t.AssignedTo.FullName : null,
                _context.TaskComments.Count(c => c.TaskItemId == t.Id),
                t.Attachments.Count(),
                t.ChecklistItems.Count(),
                t.ChecklistItems.Count(c => c.IsDone)))
            .FirstAsync();

        var checklist = await _context.TaskChecklistItems.AsNoTracking()
            .Where(c => c.TaskItemId == id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new ChecklistItemDto(c.Id, c.Text, c.IsDone, c.SortOrder))
            .ToListAsync();

        var attachmentRows = await _context.TaskAttachments.AsNoTracking()
            .Where(a => a.TaskItemId == id)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Select(a => new
            {
                a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UserId,
                UserFullName = a.User != null ? a.User.FullName : null,
                a.CreatedAt
            })
            .ToListAsync();

        // Ekler statik dosya olarak değil, yetkili indirme ucundan servis ediliyor. Adresi
        // sorgunun İÇİNDE kurmuyoruz: EF Core, metinle bir int sütununu birleştiren ifadeyi
        // ("..." + a.Id) SQL'e çeviremiyor ve sorgu çalışma anında hata veriyor.
        var attachments = attachmentRows
            .Select(a => new AttachmentDto(
                a.Id, a.FileName, TaskAttachmentsController.DownloadUrl(id, a.Id),
                a.ContentType, a.SizeBytes,
                a.UserId, a.UserFullName, a.CreatedAt))
            .ToList();

        return Ok(new TaskDetailDto(dto, checklist, attachments));
    }

    /// <summary>Görevin değişiklik geçmişi - kim, ne zaman, neyi değiştirdi.</summary>
    [HttpGet("{id}/activity")]
    public async Task<ActionResult<IEnumerable<ActivityDto>>> GetActivity(int id)
    {
        var task = await GetAccessibleTaskAsync(id, tracked: false);
        if (task == null) return NotFound();

        var activities = await _context.TaskActivities.AsNoTracking()
            .Where(a => a.TaskItemId == id)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(100)
            .Select(a => new ActivityDto(
                a.Id, a.ActivityType, a.Detail,
                a.UserId,
                a.User != null ? a.User.FullName : null,
                a.User != null ? a.User.AvatarUrl : null,
                a.CreatedAt))
            .ToListAsync();

        return Ok(activities);
    }

    // ---------------------------------------------------------------- Yardımcılar

    /// <summary>
    /// Görevi getirir ve kullanıcının ona erişim hakkı olduğunu doğrular. Erişim yoksa
    /// null döner; çağıran taraf bunu NotFound'a çeviriyor (var olduğunu bile sızdırmıyoruz).
    /// </summary>
    private async Task<TaskItem?> GetAccessibleTaskAsync(int id, bool tracked = true)
    {
        var query = tracked ? _context.Tasks : _context.Tasks.AsNoTracking();
        var task = await query.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return null;

        if (!User.IsAdmin() && task.Project?.TeamId != User.GetTeamId()) return null;
        return task;
    }

    private async Task<string?> ValidateAssigneeAsync(int? assignedToId, int projectTeamId)
    {
        if (assignedToId == null) return null;

        var assignee = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == assignedToId.Value);
        if (assignee == null) return "Atanacak kullanıcı bulunamadı.";
        if (assignee.Role != Role.Admin && assignee.TeamId != projectTeamId)
            return "Görev, projenin ekibinde olmayan bir kullanıcıya atanamaz.";
        return null;
    }

    private static string? ValidateFields(string title, string status, string priority)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Görev başlığı zorunludur.";
        if (title.Trim().Length > 200) return "Görev başlığı en fazla 200 karakter olabilir.";
        if (!ValidStatuses.Contains(status)) return "Geçersiz görev durumu.";
        if (!ValidPriorities.Contains(priority)) return "Geçersiz öncelik değeri.";
        return null;
    }

    /// <summary>Yeni görev, hedef sütunun en altına eklenir.</summary>
    private async Task<int> NextSortOrderAsync(string status)
    {
        var max = await _context.Tasks
            .Where(t => t.Status == status)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync();
        return (max ?? 0) + 1;
    }

    private async Task<string?> AssigneeNameAsync(int? userId)
    {
        if (userId == null) return null;
        return await _context.Users.AsNoTracking()
            .Where(u => u.Id == userId.Value)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync();
    }

    // ---------------------------------------------------------------- Yazma

    public record CreateTaskRequest(
        string Title, string? Description, string? Status, string? Priority,
        DateTime? DueDate, int ProjectId, int? AssignedToId);

    public record UpdateTaskRequest(
        string Title, string? Description, string Status, string? Priority,
        DateTime? DueDate, int ProjectId, int? AssignedToId);

    public record UpdateTaskStatusRequest(string Status);

    /// <summary>Bir sütunun tamamının yeni sırası. Sürükle-bırak sonrası gönderiliyor.</summary>
    public record ReorderRequest(string Status, List<int> OrderedIds);

    [HttpPost]
    public async Task<ActionResult<TaskDto>> CreateTask(CreateTaskRequest request)
    {
        if (!IsTaskManager) return Forbid();

        var project = await _context.Projects.FindAsync(request.ProjectId);
        if (project == null) return BadRequest("Geçersiz proje.");

        if (!User.IsAdmin() && project.TeamId != User.GetTeamId())
            return Forbid();

        var status = string.IsNullOrWhiteSpace(request.Status) ? "todo" : request.Status.Trim();
        var priority = string.IsNullOrWhiteSpace(request.Priority) ? "Medium" : request.Priority.Trim();

        var error = ValidateFields(request.Title, status, priority)
            ?? await ValidateAssigneeAsync(request.AssignedToId, project.TeamId);
        if (error != null) return BadRequest(error);

        var task = new TaskItem
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Status = status,
            Priority = priority,
            DueDate = request.DueDate.ToUtcSafe(),
            SortOrder = await NextSortOrderAsync(status),
            CompletedAt = status == "done" ? DateTime.UtcNow : null,
            ProjectId = project.Id,
            AssignedToId = request.AssignedToId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Tasks.Add(task);

        // İki SaveChanges tek transaction içinde. Görevi önce kaydetmek zorundayız (geçmiş
        // kaydı ve bildirim görevin Id'sine ihtiyaç duyuyor), ama ikinci kayıt patlarsa
        // ortada geçmişi ve bildirimi olmayan yarım bir görev kalmamalı.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.SaveChangesAsync();

        _activityService.Log(task.Id, User.GetUserId(), "created", "görevi oluşturdu");

        if (task.AssignedToId.HasValue && task.AssignedToId.Value != User.GetUserId())
        {
            _notificationService.Queue(
                task.AssignedToId.Value,
                $"\"{task.Title}\" adlı bir görev size atandı.",
                task.Id);
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var dto = await ToTaskDtoAsync(task.Id);
        return CreatedAtAction(nameof(GetTask), new { id = task.Id }, dto);
    }

    private async Task<TaskDto> ToTaskDtoAsync(int id) =>
        await _context.Tasks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TaskDto(
                t.Id, t.Title, t.Description, t.Status, t.Priority, t.DueDate, t.SortOrder, t.CompletedAt,
                t.ProjectId, t.Project!.Name,
                t.AssignedToId, t.AssignedTo != null ? t.AssignedTo.FullName : null,
                _context.TaskComments.Count(c => c.TaskItemId == t.Id),
                t.Attachments.Count(),
                t.ChecklistItems.Count(),
                t.ChecklistItems.Count(c => c.IsDone)))
            .FirstAsync();

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTask(int id, UpdateTaskRequest request)
    {
        // Sıradan ekip üyesi bu ucu kullanmıyor; durum değişikliği için PATCH {id}/status var.
        if (!IsTaskManager) return Forbid();

        var existing = await GetAccessibleTaskAsync(id);
        if (existing == null) return NotFound();

        var targetProject = existing.ProjectId == request.ProjectId
            ? existing.Project
            : await _context.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProjectId);

        if (targetProject == null) return BadRequest("Geçersiz proje.");
        if (!User.IsAdmin() && targetProject.TeamId != User.GetTeamId()) return Forbid();

        var priority = string.IsNullOrWhiteSpace(request.Priority) ? existing.Priority : request.Priority.Trim();
        var status = request.Status?.Trim() ?? "";

        var error = ValidateFields(request.Title, status, priority)
            ?? await ValidateAssigneeAsync(request.AssignedToId, targetProject.TeamId);
        if (error != null) return BadRequest(error);

        // Geçmiş kaydı için değişiklik öncesi durumun kopyasını alıyoruz.
        var before = new TaskItem
        {
            Id = existing.Id,
            Title = existing.Title,
            Description = existing.Description,
            Status = existing.Status,
            Priority = existing.Priority,
            DueDate = existing.DueDate,
            ProjectId = existing.ProjectId,
            AssignedToId = existing.AssignedToId
        };

        var previousAssignedToId = existing.AssignedToId;
        var previousStatus = existing.Status;

        existing.Title = request.Title.Trim();
        existing.Description = request.Description?.Trim();
        existing.Status = status;
        existing.Priority = priority;
        existing.DueDate = request.DueDate.ToUtcSafe();
        existing.ProjectId = targetProject.Id;
        existing.AssignedToId = request.AssignedToId;
        existing.ModifiedAt = DateTime.UtcNow;

        if (status != previousStatus)
        {
            existing.CompletedAt = status == "done" ? DateTime.UtcNow : null;
            existing.SortOrder = await NextSortOrderAsync(status);
        }

        var assigneeName = await AssigneeNameAsync(existing.AssignedToId);
        _activityService.LogChanges(before, existing, User.GetUserId(), assigneeName);

        if (existing.AssignedToId.HasValue
            && existing.AssignedToId != previousAssignedToId
            && existing.AssignedToId.Value != User.GetUserId())
        {
            _notificationService.Queue(
                existing.AssignedToId.Value,
                $"\"{existing.Title}\" adlı bir görev size atandı.",
                existing.Id);
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Yalnızca görevin durumunu değiştirir. Ekip üyesi sadece kendisine atanmış görevin
    /// durumunu değiştirebilir. Görev hedef sütunun en altına yerleştirilir.
    /// </summary>
    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateTaskStatus(int id, UpdateTaskStatusRequest request)
    {
        var existing = await GetAccessibleTaskAsync(id);
        if (existing == null) return NotFound();

        if (!IsTaskManager && existing.AssignedToId != User.GetUserId())
            return Forbid();

        var status = request.Status?.Trim() ?? "";
        if (!ValidStatuses.Contains(status)) return BadRequest("Geçersiz görev durumu.");

        if (existing.Status != status)
        {
            existing.Status = status;
            existing.CompletedAt = status == "done" ? DateTime.UtcNow : null;
            existing.SortOrder = await NextSortOrderAsync(status);
            existing.ModifiedAt = DateTime.UtcNow;

            _activityService.LogStatusChange(existing.Id, User.GetUserId(), status);
            await _context.SaveChangesAsync();
        }

        return NoContent();
    }

    /// <summary>
    /// Bir sütunun tamamının yeni sırasını kaydeder. Sürükle-bırak sonrasında hedef
    /// sütundaki görev kimlikleri sırayla gönderiliyor; sütunlar arası taşımada durum da
    /// burada güncelleniyor.
    ///
    /// Sıralama başkalarının kartlarının da yerini değiştirdiği için yalnızca yöneticilere
    /// açık; ekip üyesi kendi görevini PATCH {id}/status ile taşımaya devam ediyor.
    /// </summary>
    [HttpPatch("reorder")]
    public async Task<IActionResult> Reorder(ReorderRequest request)
    {
        if (!IsTaskManager) return Forbid();

        var status = request.Status?.Trim() ?? "";
        if (!ValidStatuses.Contains(status)) return BadRequest("Geçersiz görev durumu.");
        if (request.OrderedIds == null || request.OrderedIds.Count == 0) return NoContent();
        if (request.OrderedIds.Count > 500) return BadRequest("Tek seferde en fazla 500 görev sıralanabilir.");

        var ids = request.OrderedIds.Distinct().ToList();

        // Ekip izolasyonunu sorgunun içinde uyguluyoruz. Önceden önce "hepsi bulundu mu"
        // kontrol ediliyor, sonra ayrı bir Forbid dönülüyordu; bu, olmayan bir görev
        // kimliğiyle (400) başka ekibin görev kimliğini (403) ayırt etmeyi mümkün kılıyor,
        // yani başka ekipte hangi kimliklerin var olduğunu sızdırıyordu.
        var query = _context.Tasks.Where(t => ids.Contains(t.Id));

        if (!User.IsAdmin())
        {
            var teamId = User.GetTeamId();
            if (teamId == null) return NotFound("Sıralanan görevlerden bazılarına erişilemiyor.");
            query = query.Where(t => t.Project!.TeamId == teamId);
        }

        var tasks = await query.ToListAsync();

        if (tasks.Count != ids.Count)
            return NotFound("Sıralanan görevlerden bazılarına erişilemiyor.");

        var now = DateTime.UtcNow;
        var userId = User.GetUserId();

        for (var index = 0; index < ids.Count; index++)
        {
            var task = tasks.First(t => t.Id == ids[index]);

            if (task.Status != status)
            {
                task.Status = status;
                task.CompletedAt = status == "done" ? now : null;
                _activityService.LogStatusChange(task.Id, userId, status);
            }

            task.SortOrder = index;
            task.ModifiedAt = now;
        }

        // Arşivlenmiş (30 günden eski, tamamlanmış) görevler panoda görünmediği için
        // OrderedIds'e girmiyor, ama SortOrder değerleri diskte duruyor. Yukarıda görünen
        // kartlara 0..n-1 verdiğimiz için, kullanıcı "arşivi göster" dediğinde eski
        // kartlar yeni kartların arasına karışıyordu. Bu yüzden aynı sütundaki listede
        // olmayan görevleri, aralarındaki sırayı bozmadan listenin sonuna itiyoruz.
        var trailingQuery = _context.Tasks
            .Where(t => t.Status == status && !ids.Contains(t.Id));

        if (!User.IsAdmin())
        {
            var scopeTeamId = User.GetTeamId();
            trailingQuery = trailingQuery.Where(t => t.Project!.TeamId == scopeTeamId);
        }

        var trailing = await trailingQuery
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToListAsync();

        for (var index = 0; index < trailing.Count; index++)
        {
            var newOrder = ids.Count + index;
            if (trailing[index].SortOrder == newOrder) continue;

            // ModifiedAt'e dokunmuyoruz: kullanıcı bu görevleri gerçekten değiştirmedi,
            // sadece teknik bir sıra numarası kaydırması yapıldı.
            trailing[index].SortOrder = newOrder;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTask(int id)
    {
        if (!IsTaskManager) return Forbid();

        var task = await GetAccessibleTaskAsync(id);
        if (task == null) return NotFound();

        // Diskteki dosyaları da temizliyoruz; aksi halde App_Data/attachments altında
        // hiçbir kayda bağlı olmayan dosyalar birikir.
        var storedFiles = await _context.TaskAttachments
            .Where(a => a.TaskItemId == id)
            .Select(a => a.StoredFileName)
            .ToListAsync();

        _context.Tasks.Remove(task);
        await _context.SaveChangesAsync();

        // Dosya silinemezse görev yine de silinmiş olmalı; sadece artık bir dosya kalır.
        foreach (var stored in storedFiles)
            _storage.TryDelete(_storage.AttachmentsFolder, stored);

        return NoContent();
    }
}
