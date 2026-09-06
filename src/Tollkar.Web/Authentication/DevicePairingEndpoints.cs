using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Tollkar.Web.Logging;

namespace Tollkar.Web.Authentication;

public static class DevicePairingEndpoints
{
    public static void MapDevicePairingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/pairing");

        // The display is not signed in yet, so it creates and polls its own request anonymously.
        // Only the device code grants a session, and it never leaves the display outside a request body.
        group.MapPost("/requests", (HttpContext context, DevicePairing pairing) =>
        {
            var request = pairing.Create(context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            return Results.Ok(new
            {
                userCode = request.UserCode,
                deviceCode = request.DeviceCode,
                imageUrl = "/api/pairing/qr?code=" + Uri.EscapeDataString(request.UserCode),
                expiresAt = request.ExpiresAt
            });
        }).AllowAnonymous();

        group.MapGet("/qr", (string? code, HttpContext context, DevicePairing pairing) =>
            pairing.Peek(code ?? string.Empty) is PairingState.Pending or PairingState.Approved
                ? AccessLinks.QrCode(PairingUrl(context.Request, code!))
                : Results.NotFound()).AllowAnonymous();

        group.MapGet("/requests/{userCode}", (string userCode, HttpContext context, DevicePairing pairing) =>
            RejectGuest(context) ?? Describe(pairing.Peek(userCode))).RequireAuthorization();

        group.MapPost("/requests/{userCode}/approve", (string userCode, HttpContext context, DevicePairing pairing) =>
            RejectGuest(context) ??
            Confirm(pairing.Approve(userCode, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!)))
            .LogUserAction().AddEndpointFilter<ValidateAuthRequest>().RequireAuthorization();

        group.MapDelete("/requests/{userCode}", (string userCode, HttpContext context, DevicePairing pairing) =>
            RejectGuest(context) ??
            Confirm(pairing.Reject(userCode, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!)))
            .LogUserAction().AddEndpointFilter<ValidateAuthRequest>().RequireAuthorization();

        group.MapPost("/session", async (PairingSession? session, DevicePairing pairing, GuestAccess access,
            UserManager<TollkarUser> users, HttpContext context) =>
        {
            var claim = pairing.Claim(session?.UserCode, session?.DeviceCode);
            if (claim is not { State: PairingState.Approved, OwnerId: { } ownerId })
                return Describe(claim.State);
            // A deleted owner leaves nothing to join; the display simply asks for a new code.
            if (await users.FindByIdAsync(ownerId) is null)
                return Results.NotFound();

            await access.SignInAsync(context, new GuestAccess.GuestGrant(ownerId, access.ExpiresAt()));
            return Results.Ok(new { status = "approved" });
        }).AllowAnonymous();
    }

    private static IResult? RejectGuest(HttpContext context) =>
        // A guest session shares somebody else's queue and must not hand it to another device.
        context.User.HasClaim(GuestAccess.GuestClaim, bool.TrueString) ? Results.Forbid() : null;

    private static IResult Confirm(PairingState state) => state switch
    {
        PairingState.Unknown => Results.NotFound(),
        PairingState.Expired => Results.StatusCode(StatusCodes.Status410Gone),
        PairingState.Conflict => Results.StatusCode(StatusCodes.Status409Conflict),
        _ => Results.NoContent()
    };

    private static IResult Describe(PairingState state) => state switch
    {
        PairingState.Pending => Results.Ok(new { status = "pending" }),
        PairingState.Approved => Results.Ok(new { status = "approved" }),
        PairingState.Expired => Results.StatusCode(StatusCodes.Status410Gone),
        PairingState.Conflict => Results.StatusCode(StatusCodes.Status409Conflict),
        _ => Results.NotFound()
    };

    private static string PairingUrl(HttpRequest request, string userCode) =>
        AccessLinks.Absolute(request, "/pair/" + Uri.EscapeDataString(userCode));

    private sealed record PairingSession(string? UserCode, string? DeviceCode);
}
