using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using QRCoder;

namespace Tollkar.Web.Authentication;

public sealed class GuestAccess(IDataProtectionProvider dataProtection, TimeProvider timeProvider)
{
    public const string AuthenticationScheme = "Tollkar.Guest";
    public const string GuestClaim = "tollkar:guest";
    public const string ExpirationClaim = "tollkar:guest-expires";
    private readonly IDataProtector protector = dataProtection.CreateProtector("Tollkar.GuestAccess.v1");
    private readonly ConcurrentDictionary<(string OwnerId, DateOnly Date), string> tokens = new();

    public string CreateToken(string ownerId)
    {
        var date = CurrentDate();
        foreach (var key in tokens.Keys.Where(key => key.Date != date)) tokens.TryRemove(key, out _);
        return tokens.GetOrAdd((ownerId, date), key =>
            protector.Protect(JsonSerializer.Serialize(new GuestToken(key.OwnerId, key.Date))));
    }

    public GuestGrant? ValidateToken(string token)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<GuestToken>(protector.Unprotect(token));
            return payload is { OwnerId.Length: > 0 } && payload.Date == CurrentDate()
                ? new GuestGrant(payload.OwnerId, ExpirationFor(payload.Date))
                : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    public DateTimeOffset ExpiresAt()
        => ExpirationFor(CurrentDate());

    public bool IsExpired(GuestGrant grant) => grant.ExpiresAt <= timeProvider.GetUtcNow();

    private DateTimeOffset ExpirationFor(DateOnly date)
    {
        var tomorrow = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(tomorrow, timeProvider.LocalTimeZone.GetUtcOffset(tomorrow));
    }

    public DateOnly CurrentDate() => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public sealed record GuestGrant(string OwnerId, DateTimeOffset ExpiresAt);
    private sealed record GuestToken(string OwnerId, DateOnly Date);
}

public static class GuestAccessEndpoints
{
    public static void MapGuestAccessEndpoints(this WebApplication app)
    {
        app.MapGet("/api/guest/access", (HttpContext context, GuestAccess access) =>
        {
            if (context.User.HasClaim(GuestAccess.GuestClaim, bool.TrueString))
                return Results.Forbid();
            var ownerId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var url = GuestUrl(context.Request, access.CreateToken(ownerId));
            return Results.Ok(new
            {
                url,
                imageUrl = "/api/guest/qr?date=" + access.CurrentDate().ToString("yyyy-MM-dd"),
                expiresAt = access.ExpiresAt()
            });
        }).RequireAuthorization();

        app.MapGet("/api/guest/qr", (HttpContext context, GuestAccess access) =>
        {
            if (context.User.HasClaim(GuestAccess.GuestClaim, bool.TrueString))
                return Results.Forbid();

            var ownerId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var url = GuestUrl(context.Request, access.CreateToken(ownerId));
            using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M);
            using var code = new SvgQRCode(data);
            return Results.Text(code.GetGraphic(5), "image/svg+xml");
        }).RequireAuthorization();

        app.MapGet("/guest/{token}", async (string token, GuestAccess access,
            UserManager<TollkarUser> users, HttpContext context) =>
        {
            var grant = access.ValidateToken(token);
            if (grant is null || await users.FindByIdAsync(grant.OwnerId) is null || access.IsExpired(grant))
                return Results.Redirect("/login?guest=expired");

            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, grant.OwnerId),
                new Claim(ClaimTypes.Name, "Гость"),
                new Claim(GuestAccess.GuestClaim, bool.TrueString),
                new Claim(GuestAccess.ExpirationClaim,
                    grant.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
            ], GuestAccess.AuthenticationScheme);
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            await context.SignInAsync(GuestAccess.AuthenticationScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = false, ExpiresUtc = grant.ExpiresAt });
            return Results.Redirect("/queue");
        }).AllowAnonymous();
    }

    private static string GuestUrl(HttpRequest request, string token)
    {
        var guestPath = "/guest/" + Uri.EscapeDataString(token);
        return UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, guestPath);
    }
}
