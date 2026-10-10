using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

// Signed, HttpOnly browser credential. The stable server secret makes it survive
// deploys without putting identity or credentials in localStorage. Rotating the
// Discord application secret also revokes all remembered browser logins.
sealed class RememberLogin(IConfiguration configuration, IWebHostEnvironment environment)
{
    private const string CookieName = "esun10.remember";
    private readonly byte[]? key = string.IsNullOrWhiteSpace(configuration["Discord:ClientSecret"]) ? null :
        SHA256.HashData(Encoding.UTF8.GetBytes("esun10.remember.v1:" + configuration["Discord:ClientSecret"]));
    private CookieOptions Options() => new() { HttpOnly = true, Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Lax, Path = "/", IsEssential = true, MaxAge = TimeSpan.FromDays(365), Expires = DateTimeOffset.UtcNow.AddDays(365) };
    public void Issue(HttpContext context, AuthenticatedPlayer player)
    {
        if (key == null) return;
        var payload = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Ticket(player, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(365))));
        var signature = WebEncoders.Base64UrlEncode(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
        context.Response.Cookies.Append(CookieName, payload + "." + signature, Options());
    }
    public void Restore(HttpContext context)
    {
        if (key == null) return;
        if (!context.Request.Cookies.TryGetValue(CookieName, out var raw))
        {
            var id = context.Session.GetString("discord_id");
            var name = context.Session.GetString("display_name");
            if (id != null && name != null && !id.StartsWith("dev:")) Issue(context, new(id, name, context.Session.GetString("avatar_url")));
            return;
        }
        if (raw.Length > 3800) return;
        try
        {
            var pieces = raw.Split('.'); if (pieces.Length != 2) return;
            var signature = WebEncoders.Base64UrlDecode(pieces[1]);
            if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(pieces[0])))) return;
            var ticket = JsonSerializer.Deserialize<Ticket>(WebEncoders.Base64UrlDecode(pieces[0]));
            if (ticket == null || ticket.ExpiresAt <= DateTimeOffset.UtcNow || ticket.IssuedAt > DateTimeOffset.UtcNow.AddMinutes(1)) return;
            context.Session.SetString("discord_id", ticket.Player.Id);
            context.Session.SetString("display_name", ticket.Player.DisplayName);
            if (ticket.Player.AvatarUrl != null) context.Session.SetString("avatar_url", ticket.Player.AvatarUrl);
            if (DateTimeOffset.UtcNow - ticket.IssuedAt > TimeSpan.FromDays(1)) Issue(context, ticket.Player);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { }
    }
    public void Forget(HttpContext context) => context.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/", Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Lax });
    private sealed record Ticket(AuthenticatedPlayer Player, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);
}
