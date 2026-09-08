namespace GorevTakipApi.Models;

/// <summary>
/// Göreve eklenen dosya (ekran görüntüsü, log, doküman). Dosyanın kendisi
/// App_Data/attachments altında (wwwroot'un dışında, çünkü statik dosya sunumu kimlik
/// doğrulamasından önce çalışıyor), bu tabloda ise ona ait üstveri tutuluyor.
///
/// Kullanıcının verdiği ad ile diskteki ad ayrı: diskteki ad sunucuda üretiliyor ki
/// aynı adlı iki dosya birbirini ezmesin ve dosya adı üzerinden dizin dışına çıkma
/// (path traversal) mümkün olmasın.
/// </summary>
public class TaskAttachment : BaseEntity
{
    public int TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Kullanıcının bilgisayarındaki özgün dosya adı - listede bu gösteriliyor.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Sunucuda üretilen benzersiz dosya adı.</summary>
    public string StoredFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
