using Tollkar.Application.Playback.Models;

namespace Tollkar.Application.Playback;

public interface IPlayerService : IAsyncDisposable
{
    PlayerSnapshot Snapshot { get; }

    event EventHandler? SnapshotChanged;

    ValueTask PlayAsync(Guid songId, CancellationToken cancellationToken = default);

    ValueTask TogglePauseAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
