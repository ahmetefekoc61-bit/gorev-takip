namespace GorevTakipApi.Models;

public class TaskItem : AuditEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "todo";

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? AssignedToId { get; set; }
    public User? AssignedTo { get; set; }
}