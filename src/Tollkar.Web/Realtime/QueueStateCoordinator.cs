using Microsoft.AspNetCore.SignalR;
using Tollkar.Application.Queue;
using Vostok.Logging.Abstractions;

namespace Tollkar.Web.Realtime;

public sealed class QueueStateCoordinator(IHubContext<KaraokeHub> hub, ILog log,
    TimeProvider? timeProvider = null)
    : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long version;
    private readonly Dictionary<string, Guid> currentItems = new();
    private readonly Dictionary<string, Guid> retainedUntilAdvance = new();
    private readonly Dictionary<string, PlaybackTimeline> playback = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly ILog logger = log.ForContext<QueueStateCoordinator>();

    private sealed record PlaybackTimeline(long Revision, bool IsPlaying, double PositionSeconds, long Timestamp);

    private double Position(PlaybackTimeline state) => state.PositionSeconds +
        (state.IsPlaying ? clock.GetElapsedTime(state.Timestamp).TotalSeconds : 0);

    private void SetPlayback(string userId, bool isPlaying, double position) =>
        playback[userId] = new(version + 1, isPlaying, position, clock.GetTimestamp());

    public ValueTask ControlAsync(string userId, IPlaybackQueueService queue, PlaybackCommand command,
        CancellationToken cancellationToken) =>
        MutateAsync(userId, queue, async token =>
        {
            var snapshot = await CaptureAsync(userId, queue, token);
            if (snapshot.CurrentItemId is null || !playback.TryGetValue(userId, out var state)
                || state.Revision != command.Revision) return;
            switch (command.Action)
            {
                case "play":
                    SetPlayback(userId, true, Position(state));
                    break;
                case "pause":
                    SetPlayback(userId, false, Position(state));
                    break;
                case "seek":
                    SetPlayback(userId, state.IsPlaying, command.PositionSeconds);
                    break;
                case "next":
                case "ended":
                    if (command.Action == "ended" && !state.IsPlaying) return;
                    var next = snapshot.Items.SkipWhile(item => item.Id != snapshot.CurrentItemId).Skip(1).FirstOrDefault();
                    if (retainedUntilAdvance.Remove(userId, out var retainedItemId) &&
                        retainedItemId == snapshot.CurrentItemId)
                    {
                        await queue.RemoveAsync(retainedItemId, token);
                    }
                    if (next is null)
                    {
                        currentItems.Remove(userId);
                        playback.Remove(userId);
                    }
                    else
                    {
                        currentItems[userId] = next.Id;
                        SetPlayback(userId, true, 0);
                    }
                    break;
            }
        }, cancellationToken);

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
            if (items.Any(item => item.Id == queueItemId))
            {
                if (retainedUntilAdvance.TryGetValue(userId, out var retainedItemId) &&
                    retainedItemId != queueItemId)
                {
                    await queue.RemoveAsync(retainedItemId, token);
                    retainedUntilAdvance.Remove(userId);
                }
                currentItems[userId] = queueItemId;
                SetPlayback(userId, true, 0);
            }
        }, cancellationToken);

    public ValueTask ClearAsync(
        string userId,
        IPlaybackQueueService queue,
        CancellationToken cancellationToken) =>
        MutateAsync(userId, queue, async token =>
        {
            var snapshot = await CaptureAsync(userId, queue, token);
            await queue.RemoveAllExceptAsync(snapshot.CurrentItemId, token);
            if (snapshot.CurrentItemId is { } currentItemId)
            {
                retainedUntilAdvance[userId] = currentItemId;
            }
            else
            {
                retainedUntilAdvance.Remove(userId);
            }
        }, cancellationToken);

    private async Task<QueueSnapshot> CaptureAsync(string userId, IPlaybackQueueService queue,
        CancellationToken cancellationToken)
    {
        var items = await queue.GetItemsAsync(cancellationToken);
        Guid? currentId = currentItems.TryGetValue(userId, out var id) ? id : null;
        if (currentId is not null && !items.Any(item => item.Id == currentId))
        {
            currentItems.Remove(userId);
            retainedUntilAdvance.Remove(userId);
            playback.Remove(userId);
            currentId = null;
        }
        var state = playback.TryGetValue(userId, out var timeline)
            ? new PlaybackSnapshot(timeline.Revision, timeline.IsPlaying, Position(timeline)) : null;
        return new(version, items, currentId, state);
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
                logger.Error(exception, "Unable to read committed queue state; clients recover on their next snapshot refresh.");
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
            logger.Error(exception, "Unable to publish queue state; clients recover on their next snapshot refresh.");
        }
    }

    public void Dispose() => gate.Dispose();
}
