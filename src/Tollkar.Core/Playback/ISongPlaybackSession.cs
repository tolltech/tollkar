using Tollkar.Core.Songs;

namespace Tollkar.Core.Playback;

public interface ISongPlaybackSession : IAsyncDisposable
{
    Song Song { get; }
}
