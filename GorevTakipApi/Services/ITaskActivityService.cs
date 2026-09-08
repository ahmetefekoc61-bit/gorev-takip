using GorevTakipApi.Models;

namespace GorevTakipApi.Services;

/// <summary>
/// Görev üzerindeki değişiklikleri kayıt altına alır. Kayıtlar yalnızca DbContext'e
/// ekleniyor, kaydetme işini çağıran controller asıl işlemle birlikte tek bir
/// SaveChangesAsync içinde yapıyor; böylece değişiklik ile geçmiş kaydı ya birlikte
/// yazılıyor ya da hiç yazılmıyor.
/// </summary>
public interface ITaskActivityService
{
    void Log(int taskId, int? userId, string activityType, string detail);

    /// <summary>
    /// Yalnızca durum değişikliğini kaydeder. LogChanges tam bir "önceki hal" kopyası
    /// beklediği için, sadece durumun değiştiği yollarda (sürükle-bırak, sıralama) onun
    /// yerine bu kullanılıyor; aksi halde doldurulmamış alanlar farklı görünüp sahte
    /// "açıklama güncellendi" gibi kayıtlar üretiliyordu.
    /// </summary>
    void LogStatusChange(int taskId, int? userId, string newStatus);

    /// <summary>
    /// Güncelleme öncesi ve sonrası görevi karşılaştırıp değişen her alan için ayrı bir
    /// kayıt üretir. "before" nesnesi, güncelleme uygulanmadan ÖNCE alınmış bir kopya olmalı.
    /// </summary>
    void LogChanges(TaskItem before, TaskItem after, int? userId, string? assigneeName);
}
