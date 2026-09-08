using System.Net;
using System.Net.Mail;

namespace GorevTakipApi.Services;

/// <summary>
/// Gmail SMTP üzerinden şifre sıfırlama kodu gönderir.
/// Gerçek kimlik bilgileri (Email:SenderEmail, Email:SenderPassword) appsettings.json'a değil,
/// "dotnet user-secrets" ile yerel makineye kaydedilmelidir (bkz. proje notları).
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendPasswordResetCodeAsync(string toEmail, string code)
    {
        var host = _config["Email:SmtpHost"];
        var port = int.Parse(_config["Email:SmtpPort"] ?? "587");
        var senderEmail = _config["Email:SenderEmail"];
        var senderPassword = _config["Email:SenderPassword"];
        var senderName = _config["Email:SenderName"] ?? "Görev Takip";

        if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(senderPassword))
        {
            // SMTP ayarlanmadıysa uygulama çökmesin diye sadece logluyoruz.
            _logger.LogWarning(
                "E-posta ayarları (Email:SenderEmail / Email:SenderPassword) tanımlı değil. " +
                "Sıfırlama kodu gönderilemedi. 'dotnet user-secrets set' ile ayarlayın.");
            return;
        }

        using var client = new SmtpClient(host, port)
        {
            Credentials = new NetworkCredential(senderEmail, senderPassword),
            EnableSsl = true
        };

        using var message = new MailMessage
        {
            From = new MailAddress(senderEmail, senderName),
            Subject = "Görev Takip - Şifre Sıfırlama Kodu",
            Body =
                "Merhaba,\n\n" +
                $"Şifrenizi sıfırlamak için kodunuz: {code}\n\n" +
                "Bu kod 15 dakika süreyle geçerlidir. Bu isteği siz yapmadıysanız bu e-postayı görmezden gelebilirsiniz.\n\n" +
                "Görev Takip",
            IsBodyHtml = false
        };
        message.To.Add(toEmail);

        await client.SendMailAsync(message);
    }
}
