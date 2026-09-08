namespace GorevTakipApi.Models;

public class TaskItem : AuditEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "todo";

    // Low, Medium, High, Critical - board kartında rozet olarak, dashboard'da dağılım
    // grafiğinde kullanılıyor.
    public string Priority { get; set; } = "Medium";

    public DateTime? DueDate { get; set; }

    /// <summary>
    /// Görevin kendi sütunu içindeki sırası. Bu alan olmadığı için aynı sütun içinde
    /// sürükleme hiçbir şey yapmıyor, kullanıcı işleri önem sırasına dizemiyordu.
    /// Küçük değer üstte görünür.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Görevin "Tamamlandı" durumuna geçtiği an; durum başka bir şeye çevrilirse tekrar
    /// null yapılıyor. Panoda eski tamamlanmış görevleri gizlemek ve "ne zaman bitti"
    /// sorusunu cevaplamak için kullanılıyor.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? AssignedToId { get; set; }
    public User? AssignedTo { get; set; }

    public ICollection<TaskChecklistItem> ChecklistItems { get; set; } = new List<TaskChecklistItem>();
    public ICollection<TaskAttachment> Attachments { get; set; } = new List<TaskAttachment>();
}
