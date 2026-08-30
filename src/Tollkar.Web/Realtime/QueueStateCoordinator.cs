using Microsoft.AspNetCore.SignalR;
using Tollkar.Application.Queue;

namespace Tollkar.Web.Realtime;

public sealed class QueueStateCoordinator(IHubContext<KaraokeHub> hub, ILogger<QueueStateCoordinator> logger)
    : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long version;

    public async Task<QueueSnapshot> ReadAsync(IPlaybackQueueService queue, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return new(version, await queue.GetItemsAsync(cancellationToken));
        }
        finally { gate.Release(); }
    }

    public async ValueTask MutateAsync(string userId, IPlaybackQueueService queue,
        Func<CancellationToken, ValueTask> mutation, CancellationToken cancellationToken)
    {
        QueueSnapshot? snapshot = null;
        await gate.WaitAsync(cancellationToken);
        try
        {
            await mutation(cancellationToken);
            version++;
            // Once committed, client cancellation must not prevent notifying other connections.
            try
            {
                snapshot = new QueueSnapshot(version, await queue.GetItemsAsync(CancellationToken.None));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unable to read committed queue state; clients recover on their next snapshot refresh.");
            }
        }
        finally { gate.Release(); }
        if (snapshot is not null)
            await PublishAsync(token => hub.Clients.Group(KaraokeHub.UserGroup(userId))
                .SendAsync("QueueChanged", snapshot, token));
    }

    public async Task InvalidateLibraryAsync()
    {
        await gate.WaitAsync();
        try { version++; }
        finally { gate.Release(); }
        // Catalog changes can affect any queue. No user data is included in this notification.
        await PublishAsync(token => hub.Clients.All.SendAsync("QueueInvalidated", token));
    }

    private async Task PublishAsync(Func<CancellationToken, Task> publish)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await publish(timeout.Token); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to publish queue state; clients recover on their next snapshot refresh.");
        }
    }

    public void Dispose() => gate.Dispose();
}
