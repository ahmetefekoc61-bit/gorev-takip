namespace GorevTakipApi.Models;

/// <summary>
/// Bir görev üzerinde yapılan değişikliğin kaydı. "Bu görevi kim, ne zaman tamamlandı
/// yaptı?" sorusunun cevabı burada tutuluyor.
///
/// Hem makine tarafından okunabilir bir tip (ikon/renk seçmek için) hem de sunucuda
/// hazırlanmış okunabilir bir cümle saklıyoruz; böylece istemci aynı metni yeniden
/// kurmak zorunda kalmıyor ve geçmiş kaydı, sonradan alan adları değişse bile
/// yazıldığı andaki anlamını koruyor.
/// </summary>
public class TaskActivity : BaseEntity
{
    public int TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    /// <summary>İşlemi yapan kullanıcı. Kullanıcı silinirse kayıt kalsın diye null olabilir.</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>created, status, assigned, priority, duedate, title, project, comment, attachment, checklist</summary>
    public string ActivityType { get; set; } = string.Empty;

    /// <summary>Kullanıcıya gösterilecek hazır cümle. Örn: "durumu Devam Ediyor yaptı".</summary>
    public string Detail { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
