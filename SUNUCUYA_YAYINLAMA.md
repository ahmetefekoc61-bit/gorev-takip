# Görev Takip API'yi ücretsiz sunucuya yayınlama

Hedef: bilgisayarın kapalıyken de çalışan, sabit bir **https** adresi.
Kullanacağımız ücretsiz ikili:

- **Neon** → PostgreSQL veritabanı (3 GB, süresiz ücretsiz)
- **Render** → API'nin çalıştığı sunucu (Docker, ücretsiz katman)

Kodda gereken değişiklikleri yaptım; aşağısı senin panellerden tıklayacakların.

> **Render'ın kendi ücretsiz PostgreSQL'ini kullanma** — 30 gün sonra siliniyor.
> Veritabanı için Neon.

---

## 0) Önce yerelde derlensin

Kodda yaptığım değişikliklerin sorunsuz derlendiğini 20 saniyede görelim —
Render'da 10 dakika bekleyip hata almaktan iyi:

```powershell
cd "C:\Users\efe45\OneDrive\Masaüstü\proje\GorevTakipApi"
dotnet build
```

`Build succeeded` görürsen devam. Hata çıkarsa metnini bana gönder.

> Yereldeki çalışma alışkanlığın değişmiyor: `dotnet run` yine `localhost:5146`
> ve kendi PostgreSQL'ini kullanıyor. Yeni kodun bulut kısmı yalnızca `PORT`
> ortam değişkeni tanımlıyken devreye giriyor.

---

## 1) Kodu GitHub'a koy

Render dosyaları GitHub'dan çekiyor, o yüzden proje bir depoda olmalı.

Git kurulu değilse:

```powershell
winget install --id Git.Git
```

(Kurduktan sonra PowerShell'i kapatıp yeniden aç.)

GitHub'da **private** (gizli) bir depo oluştur — adı `gorev-takip` olsun.
Gizli olması önemli: `appsettings.json` içinde yerel veritabanı şifren ve örnek
JWT anahtarı duruyor.

Sonra:

```powershell
cd "C:\Users\efe45\OneDrive\Masaüstü\proje"
git init
git add .
git commit -m "Ilk surum"
git branch -M main
git remote add origin https://github.com/<KULLANICI-ADIN>/gorev-takip.git
git push -u origin main
```

Bundan sonra kodda her değişiklikte:

```powershell
git add .
git commit -m "aciklama"
git push
```

Render değişikliği görüp otomatik yeniden yayınlıyor.

---

## 2) Neon — veritabanı

1. <https://neon.com> → GitHub hesabınla giriş yap.
2. **Create project** → adı `gorev-takip`, bölge olarak Avrupa'ya yakın birini seç.
3. Açılan ekranda **Connection string**'i kopyala. Şuna benzer:

   ```
   postgresql://neondb_owner:AbC123xyz@ep-cool-name-123456.eu-central-1.aws.neon.tech/neondb?sslmode=require
   ```

Bu satırı olduğu gibi sakla — Npgsql'in beklediği biçime çevirmeyi uygulama
kendisi yapıyor, elle uğraşmana gerek yok.

---

## 3) Render — API

1. <https://render.com> → GitHub ile giriş yap.
2. **New +** → **Web Service** → GitHub deponu seç (`gorev-takip`).
3. Ayarlar:

   | Alan | Değer |
   |---|---|
   | Name | `gorev-takip-api` |
   | Language / Runtime | **Docker** |
   | Root Directory | `GorevTakipApi` |
   | Dockerfile Path | `./Dockerfile` |
   | Instance Type | **Free** |
   | Region | Frankfurt (Avrupa) |

4. **Environment Variables** bölümüne şunları ekle:

   | Anahtar | Değer |
   |---|---|
   | `ConnectionStrings__DefaultConnection` | Neon'dan aldığın `postgresql://...` satırı |
   | `Jwt__Key` | rastgele, en az 32 karakter (aşağıda üretme komutu var) |
   | `Seed__AdminEmail` | kendi e-postan |
   | `Seed__AdminPassword` | güçlü bir şifre — ilk girişte bunu kullanacaksın |
   | `Seed__AdminFullName` | Ahmet Efe Koç |

   İsteğe bağlı: web arayüzünü de yayınlarsan
   `Cors__AllowedOrigins` = `https://arayuz-adresin.com`

   Alt çizgiler **çift** — `Jwt__Key`, tek alt çizgi değil.

   Rastgele anahtar üretmek için PowerShell'de:

   ```powershell
   -join ((48..57)+(65..90)+(97..122) | Get-Random -Count 48 | ForEach-Object {[char]$_})
   ```

5. **Create Web Service** → ilk derleme 5-10 dakika sürer, logları izleyebilirsin.

---

## 4) Çalıştığını doğrula

Render sana `https://gorev-takip-api.onrender.com` gibi bir adres verir.
Tarayıcıda aç — şunu görmelisin:

```json
{"service":"Görev Takip API","status":"ok","utc":"..."}
```

Loglarda şu iki satır da olmalı:

```
Veritabanı şeması güncel.
İlk yönetici hesabı oluşturuldu: <e-postan>
```

Görünüyorsa veritabanı bağlandı, tablolar kuruldu ve yönetici hesabın hazır.

---

## 5) Mobil uygulamayı bağla

Uygulamada giriş ekranının altındaki **Sunucu** düğmesine bas, Render adresini
yapıştır (`https://gorev-takip-api.onrender.com`), **Bağlantıyı sına** → Kaydet.
Sonra `Seed__AdminEmail` / `Seed__AdminPassword` ile gir.

Adresi kalıcı olarak uygulamanın varsayılanı yapmamı istersen söyle — o zaman
mühendisin hiçbir şey yazmasına gerek kalmaz.

---

## Bilmen gereken sınırlar

**Uyku.** Ücretsiz Render servisi 15 dakika istek almazsa kapanır; sonraki ilk
istek ~50 saniye sürer. Uygulama o sırada "Sunucuya ulaşılamıyor" diyebilir —
bir kez daha denemek yeterli.

**Dosya ekleri kalıcı değil.** Görev ekleri ve profil fotoğrafları sunucunun
diskine yazılıyor, o disk her yeniden başlatmada sıfırlanıyor. Görevler,
yorumlar, kullanıcılar (Neon'da olduğu için) durur; sadece yüklenen dosyalar
gider. Kalıcı olması için ya Render'ın ücretli diski (~$7/ay) ya da dosyaları
bir nesne deposuna (Cloudflare R2'nin ücretsiz katmanı) taşımak gerekir.

**E-posta.** Şifre sıfırlama SMTP ayarı boş olduğu için çalışmaz. İstersen
`Email__SenderEmail` / `Email__SenderPassword` ortam değişkenleriyle bir Gmail
uygulama şifresi tanımlayabilirsin.

---

## Bir şeyler ters giderse

| Belirti | Sebep / çözüm |
|---|---|
| Derleme "Dockerfile not found" | Root Directory `GorevTakipApi` olmalı |
| Log'da `Jwt:Key` hatası | `Jwt__Key` ortam değişkenini eklemedin (çift alt çizgi) |
| Log'da `Veritabanı bağlantısı tanımlı değil` | `ConnectionStrings__DefaultConnection` eksik |
| `Npgsql...password authentication failed` | Neon bağlantı satırını eksik kopyalamışsın |
| Site açılıyor ama giriş "şifre hatalı" | `Seed__AdminPassword` ile giriyor musun? Yönetici zaten varsa seed çalışmaz |
| Uygulama sonsuz yükleniyor | Servis uykudadır, 1 dakika bekleyip tekrar dene |
