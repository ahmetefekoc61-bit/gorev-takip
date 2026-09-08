using Microsoft.EntityFrameworkCore;
using GorevTakipApi.Data;
using GorevTakipApi.Models;
using GorevTakipApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

// ASP.NET Core, statik dosya sunumu için WebRootPath'i (wwwroot klasörünü) builder
// oluşturulurken bir kez çözüyor. Klasör o an yoksa UseStaticFiles kalıcı olarak devre
// dışı kalıyor - sonradan klasör/oluşan dosyalar bile 404 dönüyordu. Bu yüzden klasörü
// builder'dan ÖNCE, garanti var olacak şekilde burada oluşturuyoruz.
Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars"));

var builder = WebApplication.CreateBuilder(args);

// --- Bulut sunucusu (Render, Railway, Fly...) uyumu -------------------------
// Bu platformlar dinlenecek portu PORT ortam değişkeniyle bildiriyor ve TLS'i
// kendi kenar sunucularında sonlandırıp uygulamaya düz HTTP olarak veriyor.
// PORT tanımlıysa "bir vekil sunucunun arkasındayız" diye kabul ediyoruz.
var port = Environment.GetEnvironmentVariable("PORT");
var behindProxy = !string.IsNullOrWhiteSpace(port);
if (behindProxy)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

    // X-Forwarded-Proto olmadan uygulama isteği "http" sanır; üretilen bağlantılar
    // ve güvenlik kararları yanlış olur.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Vekilin IP'si platforma göre değiştiği için beyaz listeyi boşaltıyoruz.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

// Web uygulamasının adresi ortam değişkeninden geliyor (virgülle birden fazla).
// Mobil uygulama yerel (native) olduğu için CORS'tan etkilenmiyor; bu ayar
// yalnızca tarayıcıdan açılan Angular arayüzü için gerekli.
var configuredOrigins = builder.Configuration["Cors:AllowedOrigins"];
string[] allowedOrigins = string.IsNullOrWhiteSpace(configuredOrigins)
    ? ["http://localhost:4200"]
    : configuredOrigins.Split([',', ';'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Neon, Render, Supabase gibi servisler bağlantı bilgisini
// "postgresql://kullanici:sifre@sunucu/veritabani" biçiminde veriyor.
// Npgsql bu URL biçimini anlamıyor, anahtar=değer bekliyor - burada çeviriyoruz
// ki paneldeki adresi olduğu gibi yapıştırmak yetsin.
static string BuildConnectionString(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        throw new InvalidOperationException(
            "Veritabanı bağlantısı tanımlı değil. ConnectionStrings__DefaultConnection " +
            "ortam değişkenini ayarlayın.");
    }

    var value = raw.Trim();
    var isUrl = value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
             || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);
    if (!isUrl) return value;

    var uri = new Uri(value);
    var credentials = uri.UserInfo.Split(':', 2);

    return new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = uri.AbsolutePath.Trim('/'),
        Username = Uri.UnescapeDataString(credentials[0]),
        Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty,
        // Bulut veritabanları şifreli bağlantı zorunlu tutuyor.
        SslMode = Npgsql.SslMode.Require
    }.ConnectionString;
}

var connectionString = BuildConnectionString(
    builder.Configuration.GetConnectionString("DefaultConnection"));

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IFileStorage, FileStorageService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ITaskActivityService, TaskActivityService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// JWT anahtarı user-secrets ya da ortam değişkeninden okunuyor. Anahtar kaynak koda
// yazıldığında onu gören herkes istediği rolü taşıyan geçerli bir token imzalayabilir -
// yani hesap ve şifreye hiç gerek kalmadan Admin olabilir. Bu yüzden eksikse ya da çok
// kısaysa uygulama sessizce çalışmak yerine açık bir hatayla durur.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key tanımlı değil veya 32 karakterden kısa. Ayarlamak için proje klasöründe: " +
        "dotnet user-secrets set \"Jwt:Key\" \"<en az 32 karakterlik rastgele anahtar>\"");
}

// appsettings.json'daki örnek anahtar herkese açık (depoya da giriyor). Sunucuda bu
// anahtarla çalışmak, anahtarı gören herkesin kendine Admin token'ı imzalayabilmesi
// demek - hesap ve şifreye hiç gerek kalmadan. Bu yüzden yayında engelliyoruz.
const string ornekAnahtar = "Bu_Cok_Gizli_Ve_Uzun_Bir_Anahtar_Olmali_En_Az_32_Karakter";
if (!builder.Environment.IsDevelopment() && jwtKey == ornekAnahtar)
{
    throw new InvalidOperationException(
        "Yayın ortamında örnek Jwt:Key kullanılamaz. Sunucuda Jwt__Key ortam " +
        "değişkenine rastgele, en az 32 karakterlik bir değer verin.");
}
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        // Token 8 saat geçerli ve rol/ekip bilgisi token üretilirken içine gömülüyor.
        // Bu değerleri olduğu gibi kullansaydık, ekipten çıkarılan ya da rolü düşürülen bir
        // kullanıcı elindeki token'la 8 saat boyunca eski yetkileriyle gezmeye devam ederdi;
        // silinen bir hesabın token'ı da çalışmaya devam ederdi.
        //
        // Bu yüzden her istekte kullanıcıyı veritabanından okuyup rol ve ekip claim'lerini
        // taze değerlerle değiştiriyoruz. Küçük bir sorgu maliyeti karşılığında yetki
        // değişiklikleri anında geçerli oluyor.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var identity = context.Principal?.Identity as ClaimsIdentity;
                var idClaim = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (identity == null || !int.TryParse(idClaim, out var userId))
                {
                    context.Fail("Geçersiz oturum.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var current = await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.Role, u.TeamId })
                    .FirstOrDefaultAsync();

                if (current == null)
                {
                    // Hesap silinmiş: token hâlâ imzalı ve süresi dolmamış olsa bile geçersiz.
                    context.Fail("Hesap bulunamadı.");
                    return;
                }

                // TryRemoveClaim kullanıyoruz: RemoveClaim, claim bu kimliğe ait değilse
                // istisna fırlatıyor ve o durumda her istek 500 dönerdi.
                foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList())
                    identity.TryRemoveClaim(claim);
                foreach (var claim in identity.FindAll("TeamId").ToList())
                    identity.TryRemoveClaim(claim);

                identity.AddClaim(new Claim(ClaimTypes.Role, current.Role.ToString()));
                if (current.TeamId.HasValue)
                    identity.AddClaim(new Claim("TeamId", current.TeamId.Value.ToString()));
            }
        };
    });

builder.Services.AddAuthorization();

// Add services to the container.
// TaskItem -> Project -> Tasks gibi karşılıklı navigasyon alanları JSON'a çevrilirken
// sonsuz döngüye (object cycle) girip 500 hatasına yol açıyordu; döngüleri yok sayıyoruz.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        // Role enum'ı sayı (0,1,2) yerine "Admin"/"TeamLeader"/"TeamMember" string olarak
        // gidip gelsin - frontend'de okunur/karşılaştırılabilir olması için.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Yakalanmayan istisnalarda client'a gövdesiz 500 dönüyordu; ne kullanıcıya anlamlı bir
// mesaj gidiyor ne de standart bir hata biçimi oluşuyordu.
builder.Services.AddProblemDetails();

var app = builder.Build();

// --- Veritabanı şeması ve ilk yönetici -------------------------------------
// Bulutta elle "dotnet ef database update" çalıştıracak bir yer yok; boş bir
// veritabanına açılışta migration'ları kendimiz uyguluyoruz. Yerelde varsayılan
// olarak kapalı: kendi makinende şemayı ne zaman güncelleyeceğine sen karar ver.
if (builder.Configuration.GetValue("Database:MigrateOnStartup", behindProxy))
{
    using var migrationScope = app.Services.CreateScope();
    var migrationDb = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await migrationDb.Database.MigrateAsync();
    app.Logger.LogInformation("Veritabanı şeması güncel.");
}

// Yeni kurulan bir veritabanında hiç kullanıcı olmuyor; kayıt ucu da yalnızca
// "Ekip Üyesi" oluşturduğu için kimse yönetici olamıyor ve sistem kilitli kalıyor.
// Seed:AdminEmail + Seed:AdminPassword verilmişse ilk yöneticiyi burada açıyoruz.
// Zaten bir yönetici varsa hiçbir şey yapmıyor - yani her açılışta güvenle çalışır.
await SeedFirstAdminAsync(app, builder.Configuration);

static async Task SeedFirstAdminAsync(WebApplication app, IConfiguration configuration)
{
    var email = configuration["Seed:AdminEmail"]?.Trim();
    var password = configuration["Seed:AdminPassword"];
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (await db.Users.AnyAsync(u => u.Role == Role.Admin)) return;
    if (await db.Users.AnyAsync(u => u.Email == email)) return;

    db.Users.Add(new User
    {
        FullName = configuration["Seed:AdminFullName"]?.Trim() is { Length: > 0 } name
            ? name
            : "Yönetici",
        Email = email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
        Role = Role.Admin,
        CreatedAt = DateTime.UtcNow
    });

    await db.SaveChangesAsync();
    app.Logger.LogInformation("İlk yönetici hesabı oluşturuldu: {Email}", email);
}

// Yükleme klasörlerini uygulamanın kendi kökünden (ContentRootPath) kuruyoruz; çalışma
// dizini IIS ya da Windows servisi altında farklı bir yeri gösterebiliyor.
var fileStorage = app.Services.GetRequiredService<IFileStorage>();
Directory.CreateDirectory(fileStorage.AttachmentsFolder);
Directory.CreateDirectory(fileStorage.AvatarsFolder);

// Tek seferlik taşıma: ekler eskiden wwwroot/attachments altındaydı ve oradaki dosyalar
// kimlik doğrulaması olmadan indirilebiliyordu. Klasör wwwroot dışına alındığı için,
// bu değişiklikten önce yüklenmiş dosyaları yeni konuma taşıyoruz - yoksa eski ekler
// "dosya bulunamadı" verirdi.
var legacyAttachments = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "attachments");
if (Directory.Exists(legacyAttachments))
{
    foreach (var source in Directory.GetFiles(legacyAttachments))
    {
        try
        {
            var target = Path.Combine(fileStorage.AttachmentsFolder, Path.GetFileName(source));
            if (!File.Exists(target)) File.Move(source, target);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Eski ek dosyası taşınamadı: {Dosya}", source);
        }
    }
}

// Vekil sunucunun ilettiği gerçek istemci IP'si ve şeması, diğer her şeyden önce
// okunmalı - sonraki katmanlar bu bilgiye göre karar veriyor.
if (behindProxy) app.UseForwardedHeaders();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalError");
    logger.LogError(feature?.Error, "İşlenmeyen hata: {Path}", context.Request.Path);

    // Veritabanı kısıt ihlalleri (ör. bağlı kayıtları olan bir ekibin silinmeye çalışılması)
    // sunucu hatası değil, isteğin kendisiyle ilgili bir sorun - 400 olarak dönüyoruz.
    var isDbConflict = feature?.Error is DbUpdateException;
    context.Response.StatusCode = isDbConflict ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/json; charset=utf-8";

    var message = isDbConflict
        ? "Bu kayıt, kendisine bağlı başka kayıtlar olduğu için işlenemedi."
        : "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.";

    await context.Response.WriteAsJsonAsync(new { message });
}));

app.UseCors("AllowAngular");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}
// Mobil (Flutter) uygulama, Android emülatöründen düz HTTP ile bağlanacak - kendinden imzalı
// geliştirme sertifikasını cihaza güvenilir kılmakla uğraşmamak için. Bu yönlendirme açık
// kalsaydı düz HTTP isteği yine de HTTPS'e 307 ile yönlendirilir ve aynı sertifika sorunuyla
// karşılaşırdık. Üretimde (Development değilken) yönlendirme yine zorunlu kalıyor.
//
// Bulutta ise yönlendirmeyi KAPATIYORUZ: TLS'i Render/Cloudflare kendi tarafında
// sonlandırıp uygulamaya düz HTTP veriyor. Yönlendirme açık kalsaydı gelen her
// istek "zaten https" olduğu hâlde tekrar https'e yönlendirilir, istemci sonsuz
// döngüye girerdi. Dışarıya https zorunluluğunu platform zaten uyguluyor.
if (!app.Environment.IsDevelopment() && !behindProxy)
{
    app.UseHttpsRedirection();
}

// Profil fotoğrafları wwwroot/avatars altına kaydediliyor; tarayıcıdan
// https://.../avatars/dosya.jpg şeklinde erişilebilmesi için statik dosya sunumu açık olmalı.
app.UseStaticFiles();

app.UseAuthentication();

app.UseAuthorization();

// Sunucunun ayakta olduğunu tarayıcıdan tek bakışta görebilmek ve barındırma
// platformunun sağlık kontrolüne cevap verebilmek için.
app.MapGet("/", () => Results.Ok(new
{
    service = "Görev Takip API",
    status = "ok",
    utc = DateTime.UtcNow
}));

app.MapControllers();

app.Run();

