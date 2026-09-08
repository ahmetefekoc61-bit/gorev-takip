namespace GorevTakipApi.Extensions;

/// <summary>
/// PostgreSQL'de tarih kolonları "timestamp with time zone" tipinde ve Npgsql bu kolonlara
/// SADECE Kind=Utc olan DateTime yazılmasına izin veriyor. Tarayıcıdaki &lt;input type="date"&gt;
/// alanı ise "2026-08-27" gibi saat dilimi bilgisi olmayan bir metin gönderiyor; .NET bunu
/// Kind=Unspecified olarak okuyor ve SaveChangesAsync sırasında istisna fırlıyordu
/// (bitiş tarihi verilen hiçbir görev kaydedilemiyordu).
///
/// Bu yüzden client'tan gelen her tarihi veritabanına yazmadan önce buradan geçiriyoruz.
/// </summary>
public static class DateTimeExtensions
{
    public static DateTime? ToUtcSafe(this DateTime? value)
    {
        if (value == null) return null;
        return value.Value.ToUtcSafe();
    }

    public static DateTime ToUtcSafe(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        // Saat dilimi belirtilmemişse UTC kabul ediyoruz. Alternatifi (yerel saat varsaymak)
        // sunucunun saat dilimine göre değişen, tahmin edilemez sonuçlar üretirdi.
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
