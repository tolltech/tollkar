using Tollkar.Application.Playback.Models;

namespace Tollkar.Application.Playback;

public interface IQueuePlayerService : IAsyncDisposable
{
    PlayerSnapshot Snapshot { get; }

    event EventHandler? SnapshotChanged;

    event EventHandler? QueueChanged;

    event EventHandler<QueuePlaybackFailedEventArgs>? PlaybackFailed;

    ValueTask PlayQueueItemAsync(Guid queueItemId, CancellationToken cancellationToken = default);

    ValueTask TogglePauseAsync(CancellationToken cancellationToken = default);

    ValueTask NextAsync(CancellationToken cancellationToken = default);
}

public sealed class QueuePlaybackFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception ?? throw new ArgumentNullException(nameof(exception));
}
