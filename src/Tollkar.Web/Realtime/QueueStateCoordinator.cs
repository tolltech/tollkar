using Microsoft.AspNetCore.SignalR;
using Tollkar.Application.Queue;

namespace Tollkar.Web.Realtime;

public sealed class QueueStateCoordinator(IHubContext<KaraokeHub> hub, ILogger<QueueStateCoordinator> logger)
    : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long version;
    private readonly Dictionary<string, Guid> currentItems = new();

    public async Task<QueueSnapshot> ReadAsync(string userId, IPlaybackQueueService queue, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await CaptureAsync(userId, queue, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public ValueTask PlayNowAsync(string userId, IPlaybackQueueService queue, Guid queueItemId,
        CancellationToken cancellationToken) =>
        MutateAsync(userId, queue, async token =>
        {
            var items = await queue.GetItemsAsync(token);
            if (items.Any(item => item.Id == queueItemId)) currentItems[userId] = queueItemId;
        }, cancellationToken);

    private async Task<QueueSnapshot> CaptureAsync(string userId, IPlaybackQueueService queue,
        CancellationToken cancellationToken)
    {
        var items = await queue.GetItemsAsync(cancellationToken);
        Guid? currentId = currentItems.TryGetValue(userId, out var id) ? id : null;
        if (currentId is not null && !items.Any(item => item.Id == currentId))
        {
            currentItems.Remove(userId);
            currentId = null;
        }
        return new(version, items, currentId);
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
                snapshot = await CaptureAsync(userId, queue, CancellationToken.None);
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
