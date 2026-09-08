using System.Security.Claims;
using GorevTakipApi.Models;

namespace GorevTakipApi.Extensions;

/// <summary>
/// JWT içindeki claim'lerden mevcut kullanıcı bilgisini okumak için yardımcı metodlar.
/// Ekip izolasyonu ve (ileride) rol bazlı yetkilendirme bu metodlar üzerinden yapılır.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Token içinde kullanıcı kimliği (NameIdentifier) bulunamadı.");
        return int.Parse(claim.Value);
    }

    /// <summary>
    /// Kullanıcının ekip id'si. Henüz bir ekibe atanmamışsa null döner.
    /// </summary>
    public static int? GetTeamId(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst("TeamId");
        return claim == null ? null : int.Parse(claim.Value);
    }

    public static Role GetRole(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst(ClaimTypes.Role)
            ?? throw new InvalidOperationException("Token içinde rol bilgisi bulunamadı.");
        return Enum.Parse<Role>(claim.Value);
    }

    public static bool IsAdmin(this ClaimsPrincipal user) => user.GetRole() == Role.Admin;
}
