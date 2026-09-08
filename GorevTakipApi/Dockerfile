# Görev Takip API - bulut sunucusu için görüntü (image)
#
# Render bu dosyayı kullanarak uygulamayı derleyip çalıştırıyor.
# Yapı bağlamı (build context) GorevTakipApi klasörüdür; Render panelinde
# "Root Directory" alanına GorevTakipApi yazılmalı.

# --- 1) Derleme aşaması -----------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Önce yalnızca proje dosyasını kopyalayıp paketleri indiriyoruz. Böylece kod
# değiştiğinde bu katman önbellekten geliyor ve derleme çok daha hızlı oluyor.
COPY GorevTakipApi.csproj ./
RUN dotnet restore GorevTakipApi.csproj

COPY . .
RUN dotnet publish GorevTakipApi.csproj -c Release -o /app/publish /p:UseAppHost=false

# --- 2) Çalışma aşaması -----------------------------------------------------
# Yalnızca çalışma zamanı görüntüsü: SDK'yı taşımadığımız için imaj çok küçük.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish ./

# Uygulama açılışta bu klasörlere yazıyor; imajda hazır bulunsunlar.
# DİKKAT: Render'ın ücretsiz planında disk kalıcı değildir - buraya yüklenen
# görev ekleri ve profil fotoğrafları her yeniden başlatmada silinir.
RUN mkdir -p App_Data/attachments wwwroot/avatars

ENV ASPNETCORE_ENVIRONMENT=Production
# Bilgi amaçlı; gerçek port çalışırken PORT ortam değişkeninden okunuyor.
EXPOSE 8080

ENTRYPOINT ["dotnet", "GorevTakipApi.dll"]
