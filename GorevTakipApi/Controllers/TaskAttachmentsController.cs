using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GorevTakipApi.Data;
using GorevTakipApi.Extensions;
using GorevTakipApi.Models;
using GorevTakipApi.Services;

namespace GorevTakipApi.Controllers;

/// <summary>
/// Göreve dosya ekleme. Yorumlar yalnızca düz metin olduğu için ekran görüntüsü, log ya
/// da doküman paylaşmak mümkün değildi ve bu içerikler iş bağlamının dışına taşınıyordu.
///
/// Dosyalar App_Data/attachments altına, sunucuda üretilen benzersiz bir adla kaydediliyor;
/// kullanıcının verdiği ad yalnızca veritabanında tutulup listede gösteriliyor. Böylece
/// hem aynı adlı dosyalar birbirini ezmiyor hem de dosya adı üzerinden dizin dışına
/// çıkmak (path traversal) mümkün olmuyor.
///
/// Klasör bilerek wwwroot'un DIŞINDA: statik dosya sunumu kimlik doğrulamasından önce
/// çalıştığı için wwwroot altındaki bir eki adresini bilen herkes -- başka ekipten biri,
/// hatta hiç giriş yapmamış biri -- indirebiliyordu. Artık indirme yalnızca aşağıdaki
/// yetkili uçtan geçiyor ve her istekte ekip izolasyonu yeniden doğrulanıyor.
/// </summary>
[ApiController]
[Route("api/tasks/{taskId}/attachments")]
[Authorize]
public class TaskAttachmentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITaskActivityService _activityService;
    private readonly IFileStorage _storage;

    public TaskAttachmentsController(AppDbContext context, ITaskActivityService activityService,
        IFileStorage storage)
    {
        _context = context;
        _activityService = activityService;
        _storage = storage;
    }

    private const long MaxFileBytes = 10_000_000;

    private static readonly string[] AllowedExtensions =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
        ".pdf", ".txt", ".log", ".csv", ".json", ".xml",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".zip"
    };

    public record AttachmentDto(
        int Id, string FileName, string Url, string ContentType, long SizeBytes,
        int? UserId, string? UserFullName, DateTime CreatedAt);

    /// <summary>
    /// Görevi getirir ve kullanıcının erişebildiğini doğrular. Erişemiyorsa null döner;
    /// çağıran taraf NotFound'a çeviriyor - görevin varlığını bile sızdırmıyoruz.
    /// </summary>
    private async Task<TaskItem?> GetAccessibleTaskAsync(int taskId)
    {
        var task = await _context.Tasks.AsNoTracking()
            .Include(t => t.Project)
            .FirstOrDefaultAsync(t => t.Id == taskId);
        if (task == null) return null;

        if (!User.IsAdmin() && task.Project?.TeamId != User.GetTeamId()) return null;
        return task;
    }

    /// <summary>
    /// İstemcinin kullanacağı indirme adresi. Statik dosya değil, yetkili uç.
    /// Bu metin veritabanı sorgusunda DEĞİL, sonuçlar belleğe alındıktan sonra kuruluyor:
    /// EF Core, projeksiyon içinde metinle bir int sütununu birleştiren ifadeyi
    /// ("... " + a.Id) SQL'e çeviremez ve sorgu çalışma anında hata verir.
    /// </summary>
    internal static string DownloadUrl(int taskId, int attachmentId) =>
        $"/api/tasks/{taskId}/attachments/{attachmentId}/download";

    /// <summary>
    /// Uzantıdan içerik türü. Yükleme sırasında client'ın bildirdiği ContentType'a
    /// güvenmiyoruz: ".png" uzantılı bir dosya "text/html" olarak işaretlenip indirmede
    /// tarayıcıda çalıştırılabilirdi.
    /// </summary>
    private static string SafeContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".pdf" => "application/pdf",
            ".txt" or ".log" => "text/plain; charset=utf-8",
            ".csv" => "text/csv; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".xml" => "application/xml; charset=utf-8",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AttachmentDto>>> GetAttachments(int taskId)
    {
        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();

        var rows = await _context.TaskAttachments.AsNoTracking()
            .Where(a => a.TaskItemId == taskId)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Select(a => new
            {
                a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UserId,
                UserFullName = a.User != null ? a.User.FullName : null,
                a.CreatedAt
            })
            .ToListAsync();

        // Adresi burada, veritabanından dönen satırlar üzerinde kuruyoruz.
        var items = rows
            .Select(a => new AttachmentDto(
                a.Id, a.FileName, DownloadUrl(taskId, a.Id), a.ContentType, a.SizeBytes,
                a.UserId, a.UserFullName, a.CreatedAt))
            .ToList();

        return Ok(items);
    }

    /// <summary>
    /// Dosyanın içeriğini döner. Her indirmede görev erişimi yeniden kontrol ediliyor, yani
    /// ekipten çıkarılan biri elindeki eski bağlantıyla dosyaya ulaşamıyor.
    /// </summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(int taskId, int id)
    {
        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();

        var attachment = await _context.TaskAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TaskItemId == taskId);
        if (attachment == null) return NotFound();

        // StoredFileName'i sunucu ürettiği için güvenli; yine de dizin dışına çıkan bir
        // değer veritabanına elle yazılmış olabilir diye yolu kök klasöre karşı doğruluyoruz.
        var fullPath = _storage.ResolveInside(_storage.AttachmentsFolder, attachment.StoredFileName);
        if (fullPath == null || !System.IO.File.Exists(fullPath)) return NotFound();

        // Tarayıcı dosyayı çalıştırmaya çalışmasın diye içerik türü sniff'lenmesini kapatıyoruz.
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        // İçerik türünü uzantıdan üretiyoruz, yüklerken client'ın bildirdiği değerden değil:
        // ".png" uzantılı bir dosya "text/html" içerik türüyle yüklenip sonra tarayıcıda
        // HTML olarak çalıştırılabilirdi. fileDownloadName verdiğimiz için yanıt zaten
        // "indir" olarak işaretleniyor; önizlemeyi frontend blob üzerinden yapıyor.
        return PhysicalFile(fullPath, SafeContentType(attachment.StoredFileName), attachment.FileName);
    }

    [HttpPost]
    [RequestSizeLimit(MaxFileBytes + 1_000_000)]
    public async Task<ActionResult<AttachmentDto>> Upload(int taskId, IFormFile file)
    {
        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();

        if (file == null || file.Length == 0)
            return BadRequest("Dosya seçilmedi.");

        if (file.Length > MaxFileBytes)
            return BadRequest("Dosya boyutu en fazla 10 MB olabilir.");

        // Uzantıyı kullanıcının gönderdiği addan alıyoruz ama dosyayı O adla kaydetmiyoruz.
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? "";
        if (!AllowedExtensions.Contains(extension))
            return BadRequest("Bu dosya türü desteklenmiyor.");

        var originalName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(originalName)) originalName = "dosya" + extension;
        if (originalName.Length > 260) originalName = originalName[^260..];

        var storedName = $"task-{taskId}-{Guid.NewGuid():N}{extension}";

        var folder = _storage.AttachmentsFolder;
        Directory.CreateDirectory(folder);
        var fullPath = Path.Combine(folder, storedName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var attachment = new TaskAttachment
        {
            TaskItemId = taskId,
            UserId = User.GetUserId(),
            FileName = originalName,
            StoredFileName = storedName,
            // Client'ın bildirdiği türü değil, uzantıdan üretileni saklıyoruz.
            ContentType = SafeContentType(storedName),
            SizeBytes = file.Length,
            CreatedAt = DateTime.UtcNow
        };

        _context.TaskAttachments.Add(attachment);
        _activityService.Log(taskId, User.GetUserId(), "attachment", $"\"{originalName}\" dosyasını ekledi");

        try
        {
            await _context.SaveChangesAsync();
        }
        catch
        {
            // Veritabanına yazılamadıysa diskte yetim dosya bırakmayalım.
            try { if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath); } catch { }
            throw;
        }

        var userName = await _context.Users.AsNoTracking()
            .Where(u => u.Id == attachment.UserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync();

        return Ok(new AttachmentDto(
            attachment.Id, attachment.FileName, DownloadUrl(taskId, attachment.Id),
            attachment.ContentType, attachment.SizeBytes,
            attachment.UserId, userName, attachment.CreatedAt));
    }

    /// <summary>Dosyayı yalnızca ekleyen kişi veya yönetici silebilir.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int taskId, int id)
    {
        var task = await GetAccessibleTaskAsync(taskId);
        if (task == null) return NotFound();

        var attachment = await _context.TaskAttachments
            .FirstOrDefaultAsync(a => a.Id == id && a.TaskItemId == taskId);
        if (attachment == null) return NotFound();

        var isManager = User.IsAdmin() || User.GetRole() == Role.TeamLeader;
        if (!isManager && attachment.UserId != User.GetUserId()) return Forbid();

        _context.TaskAttachments.Remove(attachment);
        _activityService.Log(taskId, User.GetUserId(), "attachment", $"\"{attachment.FileName}\" dosyasını sildi");
        await _context.SaveChangesAsync();

        // Kayıt silindi; dosya silinemezse yalnızca artık bir dosya kalır.
        _storage.TryDelete(_storage.AttachmentsFolder, attachment.StoredFileName);

        return NoContent();
    }
}
