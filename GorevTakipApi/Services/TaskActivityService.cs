using GorevTakipApi.Data;
using GorevTakipApi.Models;

namespace GorevTakipApi.Services;

public class TaskActivityService : ITaskActivityService
{
    private readonly AppDbContext _context;

    public TaskActivityService(AppDbContext context)
    {
        _context = context;
    }

    // Durum ve öncelik veritabanında İngilizce anahtar olarak tutuluyor; geçmiş kaydı
    // kullanıcıya gösterileceği için burada Türkçe karşılığına çeviriyoruz.
    private static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["todo"] = "Yapılacak",
        ["in_progress"] = "Devam Ediyor",
        ["done"] = "Tamamlandı"
    };

    private static readonly Dictionary<string, string> PriorityLabels = new()
    {
        ["Critical"] = "Kritik",
        ["High"] = "Yüksek",
        ["Medium"] = "Orta",
        ["Low"] = "Düşük"
    };

    private static string StatusLabel(string value) => StatusLabels.TryGetValue(value, out var l) ? l : value;
    private static string PriorityLabel(string value) => PriorityLabels.TryGetValue(value, out var l) ? l : value;

    public void Log(int taskId, int? userId, string activityType, string detail)
    {
        _context.TaskActivities.Add(new TaskActivity
        {
            TaskItemId = taskId,
            UserId = userId,
            ActivityType = activityType,
            // Sütun sınırını aşan bir metin SaveChanges sırasında hataya yol açar;
            // geçmiş kaydı asıl işlemi düşürmemeli.
            Detail = detail.Length > 500 ? detail[..500] : detail,
            CreatedAt = DateTime.UtcNow
        });
    }

    public void LogStatusChange(int taskId, int? userId, string newStatus)
    {
        Log(taskId, userId, "status", $"durumu \"{StatusLabel(newStatus)}\" yaptı");
    }

    public void LogChanges(TaskItem before, TaskItem after, int? userId, string? assigneeName)
    {
        if (before.Status != after.Status)
            Log(after.Id, userId, "status", $"durumu \"{StatusLabel(after.Status)}\" yaptı");

        if (before.AssignedToId != after.AssignedToId)
        {
            Log(after.Id, userId, "assigned", after.AssignedToId == null
                ? "görevin atamasını kaldırdı"
                : $"görevi {assigneeName ?? "bir kullanıcıya"} atadı");
        }

        if (before.Priority != after.Priority)
            Log(after.Id, userId, "priority", $"önceliği \"{PriorityLabel(after.Priority)}\" yaptı");

        if (before.DueDate != after.DueDate)
        {
            Log(after.Id, userId, "duedate", after.DueDate == null
                ? "bitiş tarihini kaldırdı"
                : $"bitiş tarihini {after.DueDate.Value:dd.MM.yyyy} yaptı");
        }

        if (before.Title != after.Title)
            Log(after.Id, userId, "title", $"başlığı \"{after.Title}\" olarak değiştirdi");

        if (before.Description != after.Description)
            Log(after.Id, userId, "description", "açıklamayı güncelledi");

        if (before.ProjectId != after.ProjectId)
            Log(after.Id, userId, "project", "görevi başka bir projeye taşıdı");
    }
}
