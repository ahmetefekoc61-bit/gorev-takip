namespace GorevTakipApi.Services;

/// <summary>
/// Diskteki yüklenen dosyaların (görev ekleri ve profil fotoğrafları) tek adresi.
///
/// Önceden her controller yolu kendisi <c>Directory.GetCurrentDirectory()</c> ile
/// kuruyordu. Bu, uygulama IIS altında ya da Windows servisi olarak çalıştığında yanlış
/// sonuç verir: orada çalışma dizini uygulamanın klasörü değildir, yani dosyalar bir yere
/// yazılıp başka bir yerde aranır. Doğru kaynak <see cref="IWebHostEnvironment.ContentRootPath"/>.
/// </summary>
public interface IFileStorage
{
    /// <summary>Görev ekleri. wwwroot'un DIŞINDA - bkz. sınıf açıklaması.</summary>
    string AttachmentsFolder { get; }

    /// <summary>Profil fotoğrafları. Statik dosya olarak sunulduğu için wwwroot altında.</summary>
    string AvatarsFolder { get; }

    /// <summary>Klasör içindeki bir dosyanın tam yolunu güvenli biçimde çözer; dizin dışına
    /// çıkan bir ad verilirse null döner.</summary>
    string? ResolveInside(string folder, string fileName);

    /// <summary>Dosyayı siler. Bulunamazsa ya da silinemezse sessizce geçer.</summary>
    void TryDelete(string folder, string fileName);
}

public class FileStorageService : IFileStorage
{
    private readonly string _contentRoot;

    public FileStorageService(IWebHostEnvironment environment)
    {
        _contentRoot = environment.ContentRootPath;
    }

    public string AttachmentsFolder => Path.Combine(_contentRoot, "App_Data", "attachments");

    public string AvatarsFolder => Path.Combine(_contentRoot, "wwwroot", "avatars");

    public string? ResolveInside(string folder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        // Yalnızca dosya adını alıyoruz: "../../appsettings.json" gibi bir değer
        // veritabanına elle yazılmış olsa bile klasörün dışına çıkamasın.
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name)) return null;

        var root = Path.GetFullPath(folder);
        var full = Path.GetFullPath(Path.Combine(root, name));

        // Sondaki ayraç önemli: onsuz "/veri" kökü "/veri-yedek" klasörünü de kabul ederdi.
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return null;

        return full;
    }

    public void TryDelete(string folder, string fileName)
    {
        try
        {
            var path = ResolveInside(folder, fileName);
            if (path != null && File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Dosya silinemezse yalnızca artık bir dosya kalır; asıl işlemi düşürmeye değmez.
        }
    }
}
