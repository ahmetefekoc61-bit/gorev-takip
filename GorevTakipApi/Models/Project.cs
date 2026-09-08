namespace GorevTakipApi.Models;

public class Project : AuditEntity
{
    public string Name { get; set; } = string.Empty;
    public int TeamId { get; set; }
    public Team? Team { get; set; }
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}