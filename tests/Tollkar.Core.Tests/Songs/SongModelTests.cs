using Tollkar.Core.Formats;
using Tollkar.Core.Songs;

namespace Tollkar.Core.Tests.Songs;

public sealed class SongModelTests
{
    [Fact]
    public void SongSourceRejectsMissingStableKeys()
    {
        Assert.Throws<ArgumentException>(() => new SongSource("", "/music/song.mp4"));
        Assert.Throws<ArgumentException>(() => new SongSource("video", " "));
    }

    [Fact]
    public void SongMetadataRejectsMissingTitleAndNegativeDuration()
    {
        Assert.Throws<ArgumentException>(
            () => new SongMetadata("", null, null, SongCapabilities.Video));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SongMetadata(
                "Song",
                "Artist",
                TimeSpan.FromSeconds(-1),
                SongCapabilities.Video));
    }

    [Fact]
    public void SongRejectsEmptyId()
    {
        var metadata = new SongMetadata("Song", null, null, SongCapabilities.Video);
        var source = new SongSource("video", "/music/song.mp4");

        Assert.Throws<ArgumentException>(() => new Song(Guid.Empty, metadata, source));
    }

    [Fact]
    public void FileCandidateRejectsNegativeSizeAndNormalizesTimestampToUtc()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FileCandidate("/music/song.mp4", -1, DateTimeOffset.UnixEpoch));

        var localTime = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.FromHours(4));
        var candidate = new FileCandidate("/music/song.mp4", 1, localTime);

        Assert.Equal(TimeSpan.Zero, candidate.LastWriteTimeUtc.Offset);
        Assert.Equal(localTime.UtcDateTime, candidate.LastWriteTimeUtc.UtcDateTime);
    }
}
