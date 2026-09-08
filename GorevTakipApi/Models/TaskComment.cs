namespace GorevTakipApi.Models;

/// <summary>
/// Bir görevin altına yazılan yorum. Jira/Azure DevOps'taki work item yorumlarıyla
/// aynı mantık - görevi görebilen herkes (ekip içindeki herkes) yorum yazabilir,
/// sadece görev yönetimi (oluşturma/silme/atama) yetkisi gerekmiyor.
/// </summary>
public class TaskComment : AuditEntity
{
    public int TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public string Content { get; set; } = string.Empty;
}
