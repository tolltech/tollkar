using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Tollkar.Web.Authentication;

namespace Tollkar.Web.Realtime;

[Authorize]
public sealed class KaraokeHub(SynchronizedPlaybackQueue queue, TimeProvider timeProvider) : Hub
{
    public static string UserGroup(string userId) => "karaoke:" + userId;

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier ?? throw new HubException("An authenticated user is required.");
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId), Context.ConnectionAborted);
        DisconnectGuestWhenExpired(Context, timeProvider);
        await base.OnConnectedAsync();
    }

    public Task<QueueSnapshot> GetSnapshot() =>
        queue.GetSnapshotAsync(Context.ConnectionAborted);

    private static void DisconnectGuestWhenExpired(HubCallerContext context, TimeProvider clock)
    {
        var expiration = context.User?.FindFirst(GuestAccess.ExpirationClaim)?.Value;
        if (!long.TryParse(expiration, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds)) return;
        _ = DisconnectAsync(context, DateTimeOffset.FromUnixTimeSeconds(unixSeconds), clock);
    }

    private static async Task DisconnectAsync(
        HubCallerContext context,
        DateTimeOffset expiration,
        TimeProvider clock)
    {
        var delay = expiration - clock.GetUtcNow();
        if (delay > TimeSpan.Zero)
        {
            try { await Task.Delay(delay, clock, context.ConnectionAborted); }
            catch (OperationCanceledException) { return; }
        }
        context.Abort();
    }
}
