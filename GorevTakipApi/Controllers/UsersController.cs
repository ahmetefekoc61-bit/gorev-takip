using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GorevTakipApi.Data;
using GorevTakipApi.Extensions;
using GorevTakipApi.Models;
using GorevTakipApi.Services;

namespace GorevTakipApi.Controllers;

/// <summary>
/// Kullanıcı listesi/detayında client'a dönülen alanlar. PasswordHash, ResetToken gibi
/// hassas alanlar burada YOK - frontend'e (örn. görev atama dropdown'ı) asla gitmemeli.
/// </summary>
public record UserSummaryDto(int Id, string FullName, string Email, Role Role, int? TeamId, string? AvatarUrl);

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITaskActivityService _activityService;
    private readonly IFileStorage _storage;
    private readonly INotificationService _notificationService;

    public UsersController(AppDbContext context, ITaskActivityService activityService,
        IFileStorage storage, INotificationService notificationService)
    {
        _context = context;
        _activityService = activityService;
        _storage = storage;
        _notificationService = notificationService;
    }

    private bool IsTeamLeader => User.GetRole() == Role.TeamLeader;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserSummaryDto>>> GetUsers()
    {
        var query = _context.Users.AsNoTracking().AsQueryable();

        if (!User.IsAdmin())
        {
            var teamId = User.GetTeamId();
            var myId = User.GetUserId();

            if (teamId == null)
            {
                query = query.Where(u => u.Id == myId);
            }
            else if (IsTeamLeader)
            {
                // Ekip Lideri kendi ekibine üye ekleyebiliyor; ekleyebilmesi için henüz bir
                // ekibe atanmamış kullanıcıları da görmesi gerekiyor. Önceden sorgu sadece
                // kendi ekibiyle sınırlıydı, bu yüzden "Ekibi Olmayanlar" listesi her zaman
                // boş geliyor ve ekip lideri üye ekleme işlemini hiç yapamıyordu.
                query = query.Where(u => u.TeamId == teamId || u.TeamId == null);
            }
            else
            {
                query = query.Where(u => u.TeamId == teamId);
            }
        }

        var result = await query
            .Select(u => new UserSummaryDto(u.Id, u.FullName, u.Email, u.Role, u.TeamId, u.AvatarUrl))
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserSummaryDto>> GetUser(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        // GetTeamId() ekipsiz kullanıcıda null döner ve null != null false'tur; bu yüzden
        // eski kontrol, yeni kayıt olmuş iki ekipsiz kullanıcının birbirinin ad ve
        // e-postasını okumasına izin veriyordu.
        if (!User.IsAdmin() && id != User.GetUserId())
        {
            var myTeamId = User.GetTeamId();
            if (myTeamId == null || user.TeamId != myTeamId) return Forbid();
        }

        return new UserSummaryDto(user.Id, user.FullName, user.Email, user.Role, user.TeamId, user.AvatarUrl);
    }

    /// <summary>
    /// Admin'in bir kullanıcıyı doğrudan (self-register akışı olmadan) oluşturması için.
    /// Dar kapsamlı bir DTO kullanıyoruz - client'tan çıplak "User" nesnesi kabul etmiyoruz,
    /// aksi halde biri PasswordHash/Id/CreatedAt gibi alanları doğrudan gönderip (mass
    /// assignment) hash'lenmemiş bir şifre ya da sahte bir kayıt tarihi enjekte edebilirdi.
    /// </summary>
    public record CreateUserRequest(string FullName, string Email, string Password, Role Role, int? TeamId);

    [HttpPost]
    public async Task<ActionResult<UserSummaryDto>> CreateUser(CreateUserRequest request)
    {
        if (!User.IsAdmin())
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Ad, e-posta ve şifre zorunludur.");

        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
            return BadRequest("Bu e-posta zaten kayıtlı.");

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role,
            TeamId = request.TeamId
        };

        _context.Users.Add(user);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return BadRequest("Bu e-posta zaten kayıtlı.");
        }

        var dto = new UserSummaryDto(user.Id, user.FullName, user.Email, user.Role, user.TeamId, user.AvatarUrl);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, dto);
    }

    /// <summary>
    /// Sadece ad/e-posta günceller. Önceden bu uç nokta client'tan gelen çıplak "User"
    /// nesnesini doğrudan EntityState.Modified yapıyordu - client PasswordHash/ResetToken/
    /// CreatedAt gibi alanları göndermezse (ki frontend hiç göndermiyor) bunlar entity'nin
    /// C# varsayılan değerleriyle (null/boş) eziliyordu; örneğin şifre hash'i silinip
    /// kullanıcı hesabından tamamen atılabilirdi. Rol/ekip değişikliği zaten ayrı, dar
    /// kapsamlı UpdateUserRole uç noktasından yapılıyor - burada onlara hiç dokunmuyoruz.
    /// </summary>
    public record UpdateUserRequest(string FullName, string Email);

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, UpdateUserRequest request)
    {
        var existing = await _context.Users.FindAsync(id);
        if (existing == null) return NotFound();

        // Bir kullanıcının ad/e-postasını sadece kendisi veya Admin değiştirebilir.
        // Önceden "aynı ekipte olmak" yeterliydi; bu, ekipteki herhangi birinin başkasının
        // e-postasını kendi adresiyle değiştirip "şifremi unuttum" akışıyla o hesabı ele
        // geçirmesine izin veriyordu.
        if (!User.IsAdmin() && id != User.GetUserId())
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email))
            return BadRequest("Ad ve e-posta zorunludur.");

        var fullName = request.FullName.Trim();
        var email = request.Email.Trim();

        if (fullName.Length > 150) return BadRequest("Ad en fazla 150 karakter olabilir.");
        if (email.Length > 256 || !email.Contains('@')) return BadRequest("Geçerli bir e-posta adresi girin.");

        if (await _context.Users.AnyAsync(u => u.Id != id && u.Email == email))
            return BadRequest("Bu e-posta zaten kayıtlı.");

        // E-posta değiştiyse bekleyen şifre sıfırlama kodunu geçersiz kılıyoruz: kod eski
        // adrese gönderilmişse yeni adresin sahibi onu kullanabilmemeli.
        if (!string.Equals(existing.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            existing.ResetToken = null;
            existing.ResetTokenExpiry = null;
        }

        existing.FullName = fullName;
        existing.Email = email;
        existing.ModifiedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return BadRequest("Bu e-posta zaten kayıtlı.");
        }

        return NoContent();
    }

    /// <summary>
    /// Bir kullanıcının rolünü ve/veya ekibini değiştirir. Şifre hash'i gibi hassas alanlara
    /// hiç dokunmadığı için UpdateUser'daki genel obje güncellemesinden ayrı, dar kapsamlı
    /// bir uç nokta olarak tanımlandı.
    ///
    /// Yönetici herkesi her şekilde değiştirebilir. Ekip lideri ise SADECE kendi ekibine
    /// üye ekleyebilir ya da kendi ekibinden üye çıkarabilir - rol değiştiremez, başka bir
    /// ekibe taşıyamaz, kendi ekibi/ekipsizler dışındaki kullanıcılara dokunamaz. Böylece
    /// ekip lideri kendini ya da başkasını Admin/başka ekip yaparak izolasyonu atlatamaz.
    /// </summary>
    public record UpdateUserRoleRequest(Role Role, int? TeamId);

    [HttpPut("{id}/role")]
    public async Task<IActionResult> UpdateUserRole(int id, UpdateUserRoleRequest request)
    {
        var existing = await _context.Users.FindAsync(id);
        if (existing == null) return NotFound();

        var previousTeamId = existing.TeamId;
        var previousRole = existing.Role;

        // Role bir enum (değer tipi) olduğu için model doğrulaması aralık kontrolü yapmıyor;
        // gövdeye "role": 99 yazıldığında bu değer olduğu gibi veritabanına gidiyor ve
        // kullanıcı hiçbir role uymayan, yetkisi tanımsız bir duruma düşüyordu.
        if (!Enum.IsDefined(request.Role))
            return BadRequest("Geçersiz rol.");

        if (request.TeamId.HasValue && !await _context.Teams.AnyAsync(t => t.Id == request.TeamId.Value))
            return BadRequest("Geçersiz ekip.");

        if (User.IsAdmin())
        {
            // Sistemdeki son Admin'in rolü düşürülürse kimse kullanıcı/ekip yönetemez hale gelir.
            if (existing.Role == Role.Admin && request.Role != Role.Admin
                && !await _context.Users.AnyAsync(u => u.Role == Role.Admin && u.Id != id))
                return BadRequest("Sistemdeki son yöneticinin rolü değiştirilemez.");

            existing.Role = request.Role;
            existing.TeamId = request.TeamId;
        }
        else if (User.GetRole() == Role.TeamLeader)
        {
            var myTeamId = User.GetTeamId();
            if (myTeamId == null) return Forbid();

            // Yönetici hesaplarına ekip lideri hiç dokunamaz. Aşağıdaki kural yalnızca rolün
            // DEĞİŞMESİNİ engelliyordu; ekipsiz bir Admin "ekibe ekleniyor" gibi görünüp
            // ekip liderinin ekibine taşınabiliyor, bu da o Admin'in başka ekiplerdeki
            // görev atamalarının temizlenmesine yol açıyordu.
            if (existing.Role == Role.Admin) return Forbid();

            var addingToOwnTeam = existing.TeamId == null && request.TeamId == myTeamId;
            var removingFromOwnTeam = existing.TeamId == myTeamId && request.TeamId == null;
            var noOp = existing.TeamId == myTeamId && request.TeamId == myTeamId;

            if (request.Role != existing.Role)
                return Forbid();
            if (!addingToOwnTeam && !removingFromOwnTeam && !noOp)
                return Forbid();

            existing.TeamId = request.TeamId;
        }
        else
        {
            return Forbid();
        }

        existing.ModifiedAt = DateTime.UtcNow;

        // Kullanıcının kendisi haberdar olsun: önceden rolü ya da ekibi değişen kişi
        // bunu ancak uygulamada bir şeyler yapamadığında fark ediyordu. Bildirim
        // burada yalnızca kuyruğa giriyor, aşağıdaki SaveChangesAsync ile asıl
        // değişiklikle birlikte tek işlemde yazılıyor.
        await QueueMembershipNoticeAsync(existing, previousTeamId, previousRole);

        var currentTeamId = existing.TeamId;

        // Ekip değişmediyse ortada temizlenecek bir atama yok. Bu kontrol olmadan, sadece
        // rolü değiştiren bir istek bile (örneğin ekipsiz bir kullanıcıyı Admin yapmak)
        // aşağıdaki sorguyu "bu kullanıcının BÜTÜN görevleri" haline getirip kişinin tüm
        // atamalarını sessizce siliyordu.
        //
        // Admin'ler de kapsam dışı: ValidateAssigneeAsync bir Admin'in her ekibin görevine
        // atanmasına bilerek izin veriyor ve Admin zaten bütün görevleri görebiliyor.
        if (currentTeamId == previousTeamId || existing.Role == Role.Admin)
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // Kullanıcı başka bir ekibe geçtiyse (ya da ekipten çıkarıldıysa) artık göremeyeceği
        // görevlerdeki atamasını kaldırıyoruz. Aksi halde görevler "atanmış" görünmeye devam
        // ediyor ama atanan kişi o ekibin görevlerini artık listeleyemediği için işler
        // sessizce askıda kalıyordu.
        //
        // ExecuteUpdateAsync SaveChangesAsync'ten bağımsız çalıştığı için ikisini tek bir
        // transaction'a alıyoruz: aksi halde rol yazılıp atama temizliği patlarsa (ya da
        // tersi) kullanıcı yarı taşınmış bir durumda kalıyordu.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.SaveChangesAsync();

        var orphanedQuery = currentTeamId == null
            ? _context.Tasks.Where(t => t.AssignedToId == id)
            : _context.Tasks.Where(t => t.AssignedToId == id && t.Project!.TeamId != currentTeamId);

        // Hangi görevlerin ataması kalktığını önce okuyoruz: toplu güncelleme çalıştıktan
        // sonra bu bilgi kayboluyor ve görev geçmişinde "atama neden düştü" sorusunun
        // cevabı hiçbir yerde yazmıyordu.
        var orphanedTaskIds = await orphanedQuery.Select(t => t.Id).ToListAsync();

        if (orphanedTaskIds.Count > 0)
        {
            // Güncellemeyi kimlik listesiyle yapıyoruz: yukarıdaki sorgu Projects tablosuna
            // JOIN gerektiriyor ve JOIN'li bir toplu UPDATE her sağlayıcıda güvenilir
            // biçimde çevrilmiyor. Bu hali düz bir "WHERE Id IN (...)" üretiyor.
            await _context.Tasks
                .Where(t => orphanedTaskIds.Contains(t.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AssignedToId, (int?)null));

            var actorId = User.GetUserId();
            foreach (var taskId in orphanedTaskIds)
            {
                _activityService.Log(taskId, actorId, "assigned",
                    $"{existing.FullName} ekipten ayrıldığı için görevin atamasını kaldırdı");
            }

            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        return NoContent();
    }

    private static string RoleLabel(Role role) => role switch
    {
        Role.Admin => "Yönetici",
        Role.TeamLeader => "Ekip Lideri",
        _ => "Ekip Üyesi"
    };

    /// <summary>
    /// Rol veya ekip değiştiğinde kullanıcının kendisine bildirim bırakır. Mesajda
    /// hangi ekip ve hangi sıfat olduğu açıkça yazıyor: "ekibe eklendiniz" tek başına
    /// kullanıcının yetkisinin ne olduğunu söylemiyordu.
    /// </summary>
    private async Task QueueMembershipNoticeAsync(User user, int? previousTeamId, Role previousRole)
    {
        var teamChanged = user.TeamId != previousTeamId;
        var roleChanged = user.Role != previousRole;
        if (!teamChanged && !roleChanged) return;

        // Kendi hesabını düzenleyen yöneticiye kendi işlemini bildirmiyoruz.
        if (user.Id == User.GetUserId()) return;

        var roleLabel = RoleLabel(user.Role);
        string message;

        if (teamChanged && user.TeamId != null)
        {
            var teamName = await _context.Teams
                .AsNoTracking()
                .Where(t => t.Id == user.TeamId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync();

            message = $"\"{teamName}\" ekibine {roleLabel} olarak eklendiniz.";
        }
        else if (teamChanged)
        {
            var previousName = await _context.Teams
                .AsNoTracking()
                .Where(t => t.Id == previousTeamId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync();

            message = previousName == null
                ? "Ekipten çıkarıldınız."
                : $"\"{previousName}\" ekibinden çıkarıldınız.";
        }
        else
        {
            message = $"Rolünüz \"{roleLabel}\" olarak güncellendi.";
        }

        _notificationService.Queue(user.Id, message);
    }

    private static readonly string[] AllowedAvatarExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    /// <summary>
    /// "/avatars/xxx.png" biçimindeki bir yoldan diskteki dosyayı siler.
    ///
    /// Önek kontrolü şart: AvatarUrl alanı dışarıdan gelen tam bir adres de tutabiliyor
    /// ("https://.../user-7-abcd.png"). Bu kontrol olmasa Path.GetFileName o adresten
    /// "user-7-abcd.png" üretir ve bizim klasörümüzdeki BAŞKA birinin fotoğrafı silinirdi.
    /// </summary>
    private void DeleteAvatarFile(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)) return;
        if (!avatarUrl.StartsWith("/avatars/", StringComparison.Ordinal)) return;

        _storage.TryDelete(_storage.AvatarsFolder, avatarUrl);
    }

    /// <summary>
    /// Profil fotoğrafı yükler (multipart/form-data, alan adı "file"). Dosyayı
    /// wwwroot/avatars altına kaydedip kullanıcının AvatarUrl'sini günceller.
    /// Admin herkes için, normal kullanıcı sadece kendi hesabı için yükleyebilir.
    /// </summary>
    [HttpPost("{id}/avatar")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> UploadAvatar(int id, IFormFile file)
    {
        if (!User.IsAdmin() && id != User.GetUserId())
            return Forbid();

        var existing = await _context.Users.FindAsync(id);
        if (existing == null) return NotFound();

        if (file == null || file.Length == 0)
            return BadRequest("Dosya boş olamaz.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedAvatarExtensions.Contains(ext))
            return BadRequest("Sadece jpg, png, webp veya gif yükleyebilirsin.");

        var avatarsFolder = _storage.AvatarsFolder;
        Directory.CreateDirectory(avatarsFolder);

        var fileName = $"user-{id}-{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(avatarsFolder, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var previousAvatar = existing.AvatarUrl;

        existing.AvatarUrl = $"/avatars/{fileName}";
        existing.ModifiedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch
        {
            // Kayıt güncellenemediyse az önce yazdığımız dosyayı diskte bırakmayalım.
            try { if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath); } catch { }
            throw;
        }

        // Eski fotoğraf artık hiçbir kayda bağlı değil. Her yüklemede yeni bir dosya adı
        // ürettiğimiz için, silinmediğinde wwwroot/avatars her profil güncellemesinde
        // bir dosya daha büyüyordu.
        DeleteAvatarFile(previousAvatar);

        return Ok(new { avatarUrl = existing.AvatarUrl });
    }

    /// <summary>
    /// Kullanıcıyı siler. Yalnızca Admin yapabilir: önceden kontrol sadece "aynı ekipte mi"
    /// diye bakıyordu, yani ekipteki herhangi bir üye kendi ekip liderini bile silebiliyordu.
    /// Ayrıca ekipsiz bir kullanıcı için null != null karşılaştırması false döndüğü için,
    /// yeni kayıt olmuş biri henüz ekibe atanmamış tüm kullanıcıları silebiliyordu.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        if (!User.IsAdmin()) return Forbid();

        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (id == User.GetUserId())
            return BadRequest("Kendi hesabınızı silemezsiniz.");

        if (user.Role == Role.Admin && !await _context.Users.AnyAsync(u => u.Role == Role.Admin && u.Id != id))
            return BadRequest("Sistemdeki son yönetici silinemez.");

        // Bağlı kayıtları veritabanının zincirleme silmesine bırakmıyoruz:
        // - Görevler silinmemeli, sadece ataması kalkmalı (iş kaybolmasın).
        // - Yorumlar ve bildirimler kullanıcıya ait olduğu için birlikte siliniyor.
        // Aksi halde atanmış görevi olan bir kullanıcı foreign key ihlali yüzünden
        // hiç silinemiyor (500), görevi olmayan bir kullanıcı silinince de yorumları
        // sessizce yok oluyordu.
        //
        // Bu dört adım tek bir transaction içinde: ExecuteUpdate/ExecuteDelete her biri
        // kendi başına anında commit ettiği için, aradaki bir hata kullanıcının yorumlarını
        // silinmiş ama hesabını duruyor halde bırakabiliyordu.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.Tasks
            .Where(t => t.AssignedToId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.AssignedToId, (int?)null));

        await _context.TaskComments.Where(c => c.UserId == id).ExecuteDeleteAsync();
        await _context.Notifications.Where(n => n.UserId == id).ExecuteDeleteAsync();

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        await transaction.CommitAsync();

        // Hesap gittiğine göre profil fotoğrafı da diskte kalmasın.
        DeleteAvatarFile(user.AvatarUrl);

        return NoContent();
    }
}
