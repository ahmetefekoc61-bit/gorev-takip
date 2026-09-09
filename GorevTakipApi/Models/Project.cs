namespace GorevTakipApi.Models;

public class Project : AuditEntity
{
    public string Name { get; set; } = string.Empty;
    public int TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>
    /// Projenin hedeflenen bitiş tarihi. İsteğe bağlı: tarihi belli olmayan projeler
    /// de açılabiliyor. Görevlerdeki DueDate gibi UTC olarak saklanıyor.
    /// </summary>
    public DateTime? DueDate { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}
