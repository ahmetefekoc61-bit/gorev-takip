namespace GorevTakipApi.Models;

public class User : AuditEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public Role Role { get; set; }

    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    public string? ResetToken { get; set; }
    public DateTime? ResetTokenExpiry { get; set; }

    // Kullanıcının profil fotoğrafı. Yüklenen dosyalar için "/avatars/xxx.jpg" gibi
    // bize ait bir yol, dışarıdan (örn. Wikipedia) alınan fotoğraflar için tam bir
    // https:// URL olabilir - frontend ikisini de aynı şekilde <img> ile gösteriyor.
    public string? AvatarUrl { get; set; }
}
