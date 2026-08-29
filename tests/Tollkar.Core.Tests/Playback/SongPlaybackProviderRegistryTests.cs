using Tollkar.Core.Playback;
using Tollkar.Core.Songs;

namespace Tollkar.Core.Tests.Playback;

public sealed class SongPlaybackProviderRegistryTests
{
    [Fact]
    public void FindsProviderCaseInsensitively()
    {
        var provider = new StubProvider("video");
        var registry = new SongPlaybackProviderRegistry([provider]);

        Assert.Same(provider, registry.FindProvider("VIDEO"));
    }

    [Fact]
    public void RejectsDuplicateProviderIds()
    {
        Assert.Throws<ArgumentException>(() =>
            new SongPlaybackProviderRegistry([new StubProvider("video"), new StubProvider("VIDEO")]));
    }

    private sealed class StubProvider(string id) : ISongPlaybackProvider
    {
        public string FormatProviderId { get; } = id;
        public ValueTask<ISongPlaybackSession> OpenAsync(Song song, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
