namespace GorevTakipApi.Models;

public class User : AuditEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public Role Role { get; set; }

    public int? TeamId { get; set; }
    public Team? Team { get; set; }
}