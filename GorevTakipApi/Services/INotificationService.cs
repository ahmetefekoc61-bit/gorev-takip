namespace GorevTakipApi.Services;

/// <summary>
/// Uygulama içi bildirim oluşturmak için kullanılan servis. Proje bir ekibe atandığında
/// veya bir görev birine atandığında ilgili kullanıcı(lar)a bildirim yazar.
///
/// Queue* metodları bildirimi yalnızca DbContext'e ekler, kaydetmez. Böylece asıl işlem
/// (görev/proje oluşturma) ile bildirim tek bir SaveChangesAsync içinde, atomik olarak
/// kaydediliyor: bildirim yazılamazsa asıl kayıt da geri alınıyor, asıl kayıt başarılıysa
/// bildirim de kesin yazılmış oluyor.
/// </summary>
public interface INotificationService
{
    /// <param name="taskItemId">
    /// Bildirimin işaret ettiği görev. Verildiğinde kullanıcı bildirime tıklayınca
    /// doğrudan o görevin detayına gidebiliyor.
    /// </param>
    void Queue(int userId, string message, int? taskItemId = null);

    Task QueueTeamAsync(int teamId, string message, int? excludeUserId = null, int? taskItemId = null);
}
