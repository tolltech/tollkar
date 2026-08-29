using Tollkar.Application.Playback.Models;
using Tollkar.Application.Queue;
using Tollkar.Core.Playback;

namespace Tollkar.Application.Playback;

internal sealed class QueuePlayerService : IQueuePlayerService
{
    private readonly IPlaybackQueueService _queue;
    private readonly IPlayerService _player;
    private readonly SemaphoreSlim _transportLock = new(1, 1);
    private Guid? _activeQueueItemId;
    private int _endedTransitionPending;
    private int _failureReported;

    public QueuePlayerService(IPlaybackQueueService queue, IPlayerService player)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _player.SnapshotChanged += Player_OnSnapshotChanged;
    }

    public PlayerSnapshot Snapshot => _player.Snapshot;

    public event EventHandler? SnapshotChanged;

    public event EventHandler? QueueChanged;

    public event EventHandler<QueuePlaybackFailedEventArgs>? PlaybackFailed;

    public async ValueTask PlayQueueItemAsync(
        Guid queueItemId,
        CancellationToken cancellationToken = default)
    {
        if (queueItemId == Guid.Empty)
        {
            throw new ArgumentException("Queue item ID cannot be empty.", nameof(queueItemId));
        }

        await _transportLock.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Exchange(ref _endedTransitionPending, 1);
            var item = (await _queue.GetItemsAsync(cancellationToken))
                .FirstOrDefault(candidate => candidate.Id == queueItemId) ??
                throw new KeyNotFoundException($"Queue item '{queueItemId}' was not found.");
            _activeQueueItemId = item.Id;
            await _player.PlayAsync(item.SongId, cancellationToken);
            CompleteSongStartup();
        }
        catch
        {
            _activeQueueItemId = null;
            throw;
        }
        finally
        {
            _transportLock.Release();
        }
    }

    public async ValueTask TogglePauseAsync(CancellationToken cancellationToken = default)
    {
        await _transportLock.WaitAsync(cancellationToken);
        try
        {
            if (_player.Snapshot.SongId is null)
            {
                Interlocked.Exchange(ref _endedTransitionPending, 1);
                await MoveNextCoreAsync(cancellationToken);
                return;
            }

            await _player.TogglePauseAsync(cancellationToken);
        }
        finally
        {
            _transportLock.Release();
        }
    }

    public async ValueTask NextAsync(CancellationToken cancellationToken = default)
    {
        await _transportLock.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Exchange(ref _endedTransitionPending, 1);
            await MoveNextCoreAsync(cancellationToken);
        }
        finally
        {
            _transportLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _player.SnapshotChanged -= Player_OnSnapshotChanged;
        await _transportLock.WaitAsync();
        try
        {
            await _player.DisposeAsync();
        }
        finally
        {
            _transportLock.Release();
            _transportLock.Dispose();
        }
    }

    private void Player_OnSnapshotChanged(object? sender, EventArgs eventArgs)
    {
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
        if (_player.Snapshot.State == PlaybackState.Failed &&
            Interlocked.CompareExchange(ref _failureReported, 1, 0) == 0)
        {
            PlaybackFailed?.Invoke(
                this,
                new QueuePlaybackFailedEventArgs(
                    new InvalidOperationException("The media player could not play this song.")));
        }
        if (_player.Snapshot.State == PlaybackState.Ended &&
            Interlocked.CompareExchange(ref _endedTransitionPending, 1, 0) == 0)
        {
            _ = MoveNextAfterEndAsync();
        }
    }

    private async Task MoveNextAfterEndAsync()
    {
        try
        {
            await NextAsync();
        }
        catch (Exception exception)
        {
            PlaybackFailed?.Invoke(this, new QueuePlaybackFailedEventArgs(exception));
        }
    }

    private async ValueTask MoveNextCoreAsync(CancellationToken cancellationToken)
    {
        var items = await _queue.GetItemsAsync(cancellationToken);
        var currentIndex = _activeQueueItemId is { } activeId
            ? items.ToList().FindIndex(item => item.Id == activeId)
            : -1;
        if (currentIndex >= 0)
        {
            await _queue.RemoveAsync(items[currentIndex].Id, cancellationToken);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        var remaining = await _queue.GetItemsAsync(cancellationToken);
        var nextIndex = currentIndex < 0 ? 0 : currentIndex;
        if (nextIndex >= remaining.Count)
        {
            _activeQueueItemId = null;
            await _player.StopAsync(cancellationToken);
            return;
        }

        var next = remaining[nextIndex];
        _activeQueueItemId = next.Id;
        await _player.PlayAsync(next.SongId, cancellationToken);
        CompleteSongStartup();
    }

    private void CompleteSongStartup()
    {
        Interlocked.Exchange(ref _endedTransitionPending, 0);
        Interlocked.Exchange(ref _failureReported, 0);
        if (_player.Snapshot.State == PlaybackState.Ended &&
            Interlocked.CompareExchange(ref _endedTransitionPending, 1, 0) == 0)
        {
            _ = MoveNextAfterEndAsync();
        }
    }
}
