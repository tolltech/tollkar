using Tollkar.Core.Songs;

namespace Tollkar.Core.Playback;

public interface ISongPlaybackSession : IAsyncDisposable
{
    Song Song { get; }

    PlaybackState State { get; }

    TimeSpan Position { get; }

    event EventHandler? StateChanged;

    event EventHandler? PositionChanged;

    ValueTask PlayAsync(CancellationToken cancellationToken = default);

    ValueTask PauseAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
