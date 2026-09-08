# Görev Takip

Küçük ekipler için görev yönetim sistemi. Rol tabanlı yetkilendirme, ekip
izolasyonu, kanban panosu, dosya ekleri ve denetim geçmişi içeren tam yığın
(full-stack) bir uygulama.

**Canlı API:** <https://gorev-takip-api.onrender.com>
**Mobil uygulama:** [gorev-takip-mobile](https://github.com/ahmetefekoc61-bit/gorev-takip-mobile) (Flutter)

---

## Ne yapıyor

Bir ekip lideri proje açar, görev oluşturur ve ekibine dağıtır. Ekip üyeleri
kendi panolarında görevlerini görür, durumlarını değiştirir, yorum yazar,
kontrol listesi işaretler ve dosya ekler. Her değişiklik kim/ne zaman
bilgisiyle kaydedilir.

Üç rol var ve yetkiler gerçekten ayrışıyor:

| Rol | Yapabildikleri |
|---|---|
| **Yönetici** | Her şey: kullanıcı ve ekip oluşturma, rol atama, tüm projeler |
| **Ekip Lideri** | Kendi ekibinin projeleri, görev oluşturma/atama, üye rolleri |
| **Ekip Üyesi** | Kendi ekibinin görevleri; yalnızca durum değiştirebilir |

---

## Teknolojiler

**Backend** — .NET 10 · ASP.NET Core Web API · Entity Framework Core ·
PostgreSQL · JWT (Bearer) · BCrypt

**Web** — Angular 22 (zoneless, standalone components) · TypeScript

**Mobil** — Flutter 3.47 / Dart 3.13 · Provider

**Altyapı** — Docker · Render (API) · Neon (PostgreSQL)

---

## Öne çıkan teknik kararlar

Bu bölüm, projede "çalışıyor" ile "doğru çalışıyor" arasındaki farkı oluşturan
noktalar.

**Token'daki yetki bilgisine güvenilmiyor.** JWT 8 saat geçerli ve rol/ekip
bilgisi token üretilirken içine gömülüyor. Bu değerler olduğu gibi
kullanılsaydı, ekipten çıkarılan ya da rolü düşürülen bir kullanıcı elindeki
token'la 8 saat boyunca eski yetkileriyle gezmeye devam ederdi. `OnTokenValidated`
içinde her istekte kullanıcı veritabanından okunup rol/ekip claim'leri
tazeleniyor — küçük bir sorgu maliyeti karşılığında yetki değişiklikleri anında
geçerli oluyor. Silinmiş hesabın token'ı da böylece geçersiz kalıyor.

**Dosya ekleri wwwroot dışında.** Ekler statik klasörde tutulsaydı, adresi
bilen herkes kimlik doğrulaması olmadan indirebilirdi. Ekler `App_Data`
altında duruyor ve yalnızca yetki kontrolünden geçen bir uç noktadan
sunuluyor. Dosya adı çözümlemesi `ResolveInside` ile yapılıyor: veritabanına
elle `../../appsettings.json` yazılmış olsa bile klasörün dışına çıkılamıyor.

**Yükleme sırasında istemcinin bildirdiği içerik türü kullanılmıyor.**
`.png` uzantılı bir dosya `text/html` olarak işaretlenip indirmede tarayıcıda
çalıştırılabilirdi; içerik türü uzantıdan yeniden üretiliyor.

**Kısmi güncelleme için ayrı uç nokta.** Ekip üyesi yalnızca durum
değiştirebiliyor. Tam nesneyi PUT etmek bu kurala takılıp 403 dönerdi, bu
yüzden durum değişikliği dar kapsamlı bir `PATCH /tasks/{id}/status` ile
yapılıyor. Aynı mantıkla rol/ekip değişikliği de ayrı bir uçta — böylece
istemciden gelen çıplak `User` nesnesi entity'yi ezip şifre hash'ini
silemiyor.

**Mobilde iyimser güncelleme.** Durum değiştirme, kontrol listesi işaretleme
ve sıralama arayüzde anında uygulanıyor, sunucu hata verirse eski hâline
geri alınıyor. Kullanıcı ağ gecikmesini beklemiyor.

**Bağlantı dizesi hem URL hem anahtar=değer biçimini kabul ediyor.** Neon,
Render gibi servisler `postgresql://kullanici:sifre@sunucu/db` veriyor, Npgsql
ise anahtar=değer bekliyor. Uygulama açılışta çeviriyor; panelden kopyalanan
adresi olduğu gibi yapıştırmak yetiyor.

**Şema ve ilk yönetici açılışta kuruluyor.** Bulutta elle migration
çalıştıracak bir kabuk yok; uygulama boş veritabanına migration'ları uyguluyor
ve `Seed__AdminEmail`/`Seed__AdminPassword` verilmişse ilk yöneticiyi
oluşturuyor. Yönetici zaten varsa hiçbir şey yapmıyor, yani her açılışta
güvenle çalışıyor.

---

## Yapı

```
proje/
├── GorevTakipApi/          .NET 10 Web API
│   ├── Controllers/        Auth, Tasks, Projects, Teams, Users, Notifications
│   ├── Data/               EF Core DbContext ve yapılandırmalar
│   ├── Migrations/         Veritabanı sürümleri
│   ├── Models/             Varlıklar (User, Team, Project, TaskItem...)
│   ├── Services/           Dosya deposu, bildirim, aktivite günlüğü, e-posta
│   └── Dockerfile          Bulut dağıtımı
└── gorev-takip-frontend/   Angular 22 arayüz
```

---

## Yerelde çalıştırma

Gerekenler: .NET 10 SDK, PostgreSQL, Node.js 20+

```bash
# 1) Veritabanı ayarları
cd GorevTakipApi
cp appsettings.Development.json.example appsettings.Development.json
# dosyayı açıp kendi PostgreSQL şifreni ve rastgele bir JWT anahtarı yaz

# 2) API
dotnet run
# http://localhost:5146

# 3) Web arayüzü
cd ../gorev-takip-frontend
npm install
npm start
# http://localhost:4200
```

Şema ilk çalıştırmada otomatik kurulmuyor (yerelde kapalı). Bir kez:

```bash
dotnet ef database update
```

---

## Dağıtım

API, Docker imajı olarak Render'da; veritabanı Neon'da. Yapılandırmanın tamamı
ortam değişkenlerinden geliyor:

| Değişken | Açıklama |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL adresi (URL biçimi de kabul edilir) |
| `Jwt__Key` | Token imzalama anahtarı (min. 32 karakter) |
| `Seed__AdminEmail` / `Seed__AdminPassword` | İlk yönetici hesabı |
| `Cors__AllowedOrigins` | Web arayüzünün adresi (virgülle çoklu) |

Adım adım kurulum: [`SUNUCUYA_YAYINLAMA.md`](SUNUCUYA_YAYINLAMA.md)

---

## Bilinen sınırlar

- Ücretsiz barındırma katmanında disk kalıcı değil; yüklenen dosyalar sunucu
  yeniden başladığında siliniyor. Kalıcılık için nesne deposu (S3/R2) gerekiyor.
- Şifre sıfırlama e-postası SMTP yapılandırması olmadan çalışmıyor.
- Ücretsiz sunucu 15 dakika hareketsizlikte uykuya geçiyor; ilk istek ~50 sn sürüyor.
