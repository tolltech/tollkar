using Tollkar.Core.Songs;
using Tollkar.Infrastructure.Playback;

namespace Tollkar.Infrastructure.Tests.Playback;

public sealed class FfplayPlaybackProviderTests
{
    [Fact]
    public void ConfiguresResumeFromSavedPosition()
    {
        var song = new Song(
            Guid.NewGuid(),
            new SongMetadata("Song - demo", "Artist", null, SongCapabilities.Video),
            new SongSource("video", "/media/song with spaces.mp4"));

        var startInfo = FfplayPlaybackProvider.CreateStartInfo(
            "/tools/ffplay",
            song,
            TimeSpan.FromSeconds(12.5));

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("/tools/ffplay", startInfo.FileName);
        Assert.Equal(
            [
                "-autoexit",
                "-loglevel",
                "error",
                "-nostats",
                "-window_title",
                "Tollkar — Song - demo",
                "-ss",
                "12.5",
                "-i",
                "/media/song with spaces.mp4"
            ],
            startInfo.ArgumentList);
    }
}
