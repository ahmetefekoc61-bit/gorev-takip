namespace GorevTakipApi.Models;

/// <summary>
/// Bir görevin alt adımı. "Login ekranı" gibi bir görevin beş adımı için beş ayrı görev
/// açıldığında aralarındaki bağ kayboluyordu; kontrol listesi bu bağı koruyor.
/// </summary>
public class TaskChecklistItem : BaseEntity
{
    public int TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }

    /// <summary>Listedeki sırası. Kullanıcı adımları sürükleyerek yeniden sıralayabilir.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
