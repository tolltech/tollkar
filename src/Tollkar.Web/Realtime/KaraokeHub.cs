using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Tollkar.Web.Realtime;

[Authorize]
public sealed class KaraokeHub(SynchronizedPlaybackQueue queue) : Hub
{
    public static string UserGroup(string userId) => "karaoke:" + userId;

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier ?? throw new HubException("An authenticated user is required.");
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public Task<QueueSnapshot> GetSnapshot() =>
        queue.GetSnapshotAsync(Context.ConnectionAborted);
}
