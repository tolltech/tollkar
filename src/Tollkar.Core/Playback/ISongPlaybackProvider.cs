using Tollkar.Core.Songs;

namespace Tollkar.Core.Playback;

public interface ISongPlaybackProvider
{
    string FormatProviderId { get; }

    ValueTask<ISongPlaybackSession> OpenAsync(
        Song song,
        CancellationToken cancellationToken = default);
}
