namespace GorevTakipApi.Models;

/// <summary>
/// Bir kullanıcıya gösterilecek uygulama içi bildirim (proje ekibe atandığında,
/// görev birine atandığında vb. otomatik oluşturuluyor).
/// </summary>
public class Notification : AuditEntity
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;

    /// <summary>
    /// Bildirimin işaret ettiği görev. Bu alan olmadığı için bildirime tıklamak yalnızca
    /// "okundu" işaretliyor, kullanıcıyı ilgili göreve götüremiyordu. Görev silinirse
    /// bildirim kalsın diye null olabilir.
    /// </summary>
    public int? TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }
}
