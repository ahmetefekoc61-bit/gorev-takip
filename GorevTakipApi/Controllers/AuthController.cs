using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GorevTakipApi.Data;
using GorevTakipApi.Models;
using GorevTakipApi.Services;

namespace GorevTakipApi.Controllers;

// Doğrulama nitelikleri olmadan boş ("") veya sadece boşluktan oluşan alanlar kabul
// ediliyordu; boş şifreyle hesap açılıp yine boş şifreyle giriş yapmak mümkündü.
// [ApiController] sayesinde bu nitelikler otomatik olarak 400 üretir.
public class RegisterRequest
{
    [Required(ErrorMessage = "Ad soyad zorunludur.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Ad soyad 2-150 karakter olmalıdır.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
    public string Password { get; set; } = string.Empty;
}

public class LoginRequest
{
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunludur.")]
    public string Password { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Mevcut şifre zorunludur.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni şifre zorunludur.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
    public string NewPassword { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kod zorunludur.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni şifre zorunludur.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
    public string NewPassword { get; set; } = string.Empty;
}

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AppDbContext context, IConfiguration config, IEmailService emailService,
        ILogger<AuthController> logger)
    {
        _context = context;
        _config = config;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<User>> Register(RegisterRequest request)
    {
        var email = request.Email.Trim();

        if (await _context.Users.AnyAsync(u => u.Email == email))
            return BadRequest("Bu e-posta zaten kayıtlı.");

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = Role.TeamMember,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Yukarıdaki AnyAsync kontrolüne rağmen aynı e-postayla eşzamanlı iki kayıt
            // isteği gelirse DB'deki unique index bunu burada yakalar.
            return BadRequest("Bu e-posta zaten kayıtlı.");
        }

        return Ok(new { user.Id, user.FullName, user.Email, user.Role });
    }

    [HttpPost("login")]
    public async Task<ActionResult> Login(LoginRequest request)
    {
        // Kayıtta e-posta Trim ediliyordu ama girişte edilmiyordu; başında/sonunda boşluk
        // olan bir girişte kullanıcı hesabı bulunamıyordu.
        var email = request.Email.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized("E-posta veya şifre hatalı.");

        var token = GenerateJwtToken(user);
        return Ok(new { token });
    }

    [HttpPost("change-password")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.Users.FindAsync(userId);

        if (user == null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return BadRequest("Mevcut şifre hatalı.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Şifre başarıyla değiştirildi." });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        // Kullanıcı kayıtlı olsun ya da olmasın aynı mesajı döndürüyoruz;
        // aksi halde bu endpoint hangi e-postaların sistemde kayıtlı olduğunu ifşa eder.
        const string genericMessage = "Eğer bu e-posta kayıtlıysa, sıfırlama kodu e-posta adresinize gönderildi.";

        var email = request.Email.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
            return Ok(new { message = genericMessage });

        // Kodu kriptografik olarak güvenli üretiyoruz. System.Random tahmin edilebilir bir
        // dizi ürettiği için, saldırgan bir kez kod alıp sonraki kodları hesaplayabiliyordu.
        var token = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        // Kodun kendisini değil hash'ini saklıyoruz: veritabanını okuyabilen biri
        // (log, yedek, sızıntı) doğrudan başkasının şifresini sıfırlayamasın.
        user.ResetToken = HashResetToken(token);
        user.ResetTokenExpiry = DateTime.UtcNow.AddMinutes(15);
        await _context.SaveChangesAsync();

        // E-posta gönderimi başarısız olursa istek düşmemeli: aksi halde kayıtlı e-posta 500,
        // kayıtsız e-posta 200 döndüğü için yukarıdaki "hangi e-posta kayıtlı" koruması da
        // anlamsız hale geliyordu.
        try
        {
            await _emailService.SendPasswordResetCodeAsync(user.Email, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Şifre sıfırlama kodu gönderilemedi.");
        }

        return Ok(new { message = genericMessage });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var email = request.Email.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user?.ResetToken == null || user.ResetTokenExpiry < DateTime.UtcNow)
            return BadRequest("Kod geçersiz veya süresi dolmuş.");

        // Sabit zamanlı karşılaştırma: normal string karşılaştırması ilk farklı karakterde
        // durduğu için, yanıt süresinden kodun kaç hanesinin doğru olduğu çıkarılabilirdi.
        var providedHash = HashResetToken(request.Token.Trim());
        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedHash),
            Encoding.UTF8.GetBytes(user.ResetToken));

        if (!matches)
        {
            // Yanlış kod girildiğinde token'ı iptal ediyoruz. 6 haneli bir kod, deneme
            // sınırı olmadan 15 dakika içinde kaba kuvvetle bulunabilirdi.
            user.ResetToken = null;
            user.ResetTokenExpiry = null;
            await _context.SaveChangesAsync();
            return BadRequest("Kod geçersiz veya süresi dolmuş.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.ResetToken = null;
        user.ResetTokenExpiry = null;
        user.ModifiedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Şifre başarıyla sıfırlandı." });
    }

    private static string HashResetToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private string GenerateJwtToken(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        // Ekip izolasyonu için: kullanıcı bir ekibe atanmışsa TeamId claim'ini token'a ekle.
        if (user.TeamId.HasValue)
        {
            claims.Add(new Claim("TeamId", user.TeamId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
