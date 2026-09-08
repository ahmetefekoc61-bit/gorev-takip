# Projeleri GitHub'a koyup CV'de göstermek

Amaç: CV'deki GitHub linkine tıklayan biri, iki temiz depo ve düzgün birer
README görsün.

---

## 1) API + Web deposunu güncelle ve public yap

Gizli bilgileri temizledim: `appsettings.json` artık örnek değer içeriyor,
kendi yerel şifren `appsettings.Development.json`'a taşındı ve o dosya
`.gitignore`'a eklendi. Ayrıca depoya bir `README.md` koydum.

```powershell
cd "C:\Users\efe45\OneDrive\Masaüstü\proje"

# Daha önce takip edilen gizli dosyayı takipten çıkar (dosya diskte kalır)
git rm --cached GorevTakipApi/appsettings.Development.json

git add .
git commit -m "Add README, move local secrets out of the repo"
git push
```

Sonra GitHub'da depo sayfası → **Settings** → en altta **Danger Zone** →
**Change repository visibility** → **Make public**.

> Geçmişte yerel PostgreSQL şifren (`efe`) duruyor. Yalnızca senin
> bilgisayarındaki veritabanına ait, dışarıdan işe yaramaz. Yine de rahat
> etmek istersen yerel PostgreSQL şifreni değiştirebilirsin.

---

## 2) Mobil uygulama için ayrı depo

Flutter projesi başka klasörde (`C:\src\gorev_takip_mobile`), o yüzden kendi
deposu olacak. Buna da `README.md` ve `.gitignore` ekledim.

GitHub'da yeni bir depo aç: **gorev-takip-mobile**, **Public**, hiçbir kutuyu
işaretleme.

```powershell
cd C:\src\gorev_takip_mobile
git init
git add .
git commit -m "Ilk surum"
git branch -M main
git remote add origin https://github.com/ahmetefekoc61-bit/gorev-takip-mobile.git
git push -u origin main
```

---

## 3) Ekran görüntüleri ekle (en çok fark yaratan adım)

Kod okumadan önce herkes ekrana bakıyor. Emülatörden 4-5 görüntü al
(giriş, özet, pano, görev detayı, ekip yönetimi), sonra:

```powershell
mkdir C:\src\gorev_takip_mobile\docs
# görüntüleri buraya kopyala: 01-giris.png, 02-ozet.png, ...
```

`README.md` içindeki "Ekranlar" tablosunun altına ekle:

```markdown
<p align="center">
  <img src="docs/02-ozet.png" width="220">
  <img src="docs/03-pano.png" width="220">
  <img src="docs/04-detay.png" width="220">
</p>
```

Sonra `git add . && git commit -m "Add screenshots" && git push`.

---

## 4) GitHub profilini düzenle

**Depoları sabitle:** profil sayfan → **Customize your pins** → iki depoyu seç.
CV'den gelen kişi ilk bunları görür.

**Profil README'si:** `ahmetefekoc61-bit` adında bir depo aç (kullanıcı adınla
birebir aynı), içine `README.md` koy. Bu dosya profil sayfanın en üstünde
görünür. Kısa tut:

```markdown
# Ahmet Efe Koç

Bilgisayar mühendisliği öğrencisi. .NET, Angular ve Flutter ile tam yığın
uygulamalar geliştiriyorum.

**Görev Takip** — Ekipler için görev yönetim sistemi
· [API + Web](https://github.com/ahmetefekoc61-bit/gorev-takip)
· [Mobil](https://github.com/ahmetefekoc61-bit/gorev-takip-mobile)
· .NET 10 · Angular 22 · Flutter · PostgreSQL · Docker

📫 ahmetefekoc61@gmail.com
```

**Profil ayarları:** Bio, konum ve varsa LinkedIn linkini doldur.

---

## 5) CV'ye ne yazmalı

Sadece link koyma, projeyi bir satırla anlat:

> **Görev Takip** — Ekipler için rol tabanlı görev yönetim sistemi.
> .NET 10 Web API + PostgreSQL, Angular 22 web arayüzü ve Flutter mobil
> uygulama. JWT kimlik doğrulama, ekip bazlı yetki izolasyonu, dosya ekleri
> ve denetim geçmişi. Docker ile bulutta yayında.
> github.com/ahmetefekoc61-bit/gorev-takip

Canlı adresi de eklemek istersen: `gorev-takip-api.onrender.com`

---

## Depoya girmemesi gerekenler

`.gitignore` bunları zaten dışarıda tutuyor, ama bilerek dur:

- `appsettings.Development.json` — yerel veritabanı şifren
- `android/key.properties`, `*.jks` — mağaza imza anahtarın
- `App_Data/`, `wwwroot/avatars/` — kullanıcıların yüklediği dosyalar
- `node_modules/`, `bin/`, `obj/`, `build/` — derleme çıktıları

Bir gizli bilgi yanlışlıkla push edilirse: dosyayı düzeltmek yetmez, **o
bilgiyi değiştir** (şifreyi/anahtarı yenile). Geçmişte durmaya devam eder.
