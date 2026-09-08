using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GorevTakipApi.Data;
using GorevTakipApi.Extensions;
using GorevTakipApi.Models;
using GorevTakipApi.Services;

namespace GorevTakipApi.Controllers;

/// <summary>
/// Görevin alt adımları. "Login ekranı" gibi bir görev için beş ayrı görev açıldığında
/// aralarındaki bağ kayboluyordu; kontrol listesi adımları görevin içinde tutuyor.
///
/// Adım işaretlemek görevi ilerletmenin bir parçası olduğu için ekip üyeleri de
/// kullanabiliyor; adım ekleme/silme ise görevin kapsamını değiştirdiğinden yöneticiye
/// ve göreve atanmış kişiye açık.
/// </summary>
[ApiController]
[Route("api/tasks/{taskId}/checklist")]
[Authorize]
public class TaskChecklistController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITaskActivityService _activityService;

    public TaskChecklistController(AppDbContext context, ITaskActivityService activityService)
    {
        _context = context;
        _activityService = activityService;
    }

    public record ChecklistItemDto(int Id, string Text, bool IsDone, int SortOrder);
    public record AddChecklistItemRequest(string Text);
    public record UpdateChecklistItemRequest(string? Text, bool? IsDone);
    public record ReorderChecklistRequest(List<int> OrderedIds);

    private async Task<TaskItem?> GetAccessibleTaskAsync(int taskId)
    {
        var task = await _context.Tasks.AsNoTracking()
            .Include(t => t.Project)
            .FirstOrDefaultAsync(t => t.Id == taskId);
        if (task == null) return null;

        if (!User.IsAdmin() && task.Project?.TeamId != User.GetTeamId()) return null;
        return task;
    }

    private bool IsManager => User.IsAdmin() || User.GetRole() == Role.TeamLeader;

    /// <summary>Adım ekleyip silebilenler: yönetici ve göreve atanmış kişi.</summary>
    private bool CanEditStructure(TaskItem task) =>
        IsManager || task.AssignedToId == User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ChecklistItemDto>>> GetItems(int taskId)
    {
        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();

        var items = await _context.TaskChecklistItems.AsNoTracking()
            .Where(c => c.TaskItemId == taskId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new ChecklistItemDto(c.Id, c.Text, c.IsDone, c.SortOrder))
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<ChecklistItemDto>> AddItem(int taskId, AddChecklistItemRequest request)
    {
        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();
        if (!CanEditStructure(task)) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest("Adım metni boş olamaz.");

        var text = request.Text.Trim();
        if (text.Length > 300) return BadRequest("Adım metni en fazla 300 karakter olabilir.");

        var maxOrder = await _context.TaskChecklistItems
            .Where(c => c.TaskItemId == taskId)
            .Select(c => (int?)c.SortOrder)
            .MaxAsync();

        var item = new TaskChecklistItem
        {
            TaskItemId = taskId,
            Text = text,
            IsDone = false,
            SortOrder = (maxOrder ?? -1) + 1,
            CreatedAt = DateTime.UtcNow
        };

        _context.TaskChecklistItems.Add(item);
        _activityService.Log(taskId, User.GetUserId(), "checklist", $"\"{text}\" adımını ekledi");
        await _context.SaveChangesAsync();

        return Ok(new ChecklistItemDto(item.Id, item.Text, item.IsDone, item.SortOrder));
    }

    /// <summary>
    /// Adımı günceller. Yalnızca işaretlemek (IsDone) görevi görebilen herkese açık;
    /// metni değiştirmek görevin kapsamını değiştirdiği için yetki gerektiriyor.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateItem(int taskId, int id, UpdateChecklistItemRequest request)
    {
        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();

        var item = await _context.TaskChecklistItems
            .FirstOrDefaultAsync(c => c.Id == id && c.TaskItemId == taskId);
        if (item == null) return NotFound();

        if (request.Text != null)
        {
            if (!CanEditStructure(task)) return Forbid();
            if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest("Adım metni boş olamaz.");
            var text = request.Text.Trim();
            if (text.Length > 300) return BadRequest("Adım metni en fazla 300 karakter olabilir.");
            item.Text = text;
        }

        if (request.IsDone.HasValue && request.IsDone.Value != item.IsDone)
        {
            item.IsDone = request.IsDone.Value;
            _activityService.Log(taskId, User.GetUserId(), "checklist",
                item.IsDone ? $"\"{item.Text}\" adımını tamamladı" : $"\"{item.Text}\" adımını geri açtı");
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteItem(int taskId, int id)
    {
        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();
        if (!CanEditStructure(task)) return Forbid();

        var item = await _context.TaskChecklistItems
            .FirstOrDefaultAsync(c => c.Id == id && c.TaskItemId == taskId);
        if (item == null) return NotFound();

        _context.TaskChecklistItems.Remove(item);
        _activityService.Log(taskId, User.GetUserId(), "checklist", $"\"{item.Text}\" adımını sildi");
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("reorder")]
    public async Task<IActionResult> Reorder(int taskId, ReorderChecklistRequest request)
    {
        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();
        if (!CanEditStructure(task)) return Forbid();

        if (request.OrderedIds == null || request.OrderedIds.Count == 0) return NoContent();

        var ids = request.OrderedIds.Distinct().ToList();
        var items = await _context.TaskChecklistItems
            .Where(c => c.TaskItemId == taskId && ids.Contains(c.Id))
            .ToListAsync();

        if (items.Count != ids.Count) return BadRequest("Sıralanan adımlardan bazıları bulunamadı.");

        for (var index = 0; index < ids.Count; index++)
        {
            items.First(c => c.Id == ids[index]).SortOrder = index;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }
}
