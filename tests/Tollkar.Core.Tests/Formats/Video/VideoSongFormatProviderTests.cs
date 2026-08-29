using Tollkar.Core.Formats;
using Tollkar.Core.Formats.Video;
using Tollkar.Core.Songs;

namespace Tollkar.Core.Tests.Formats.Video;

public sealed class VideoSongFormatProviderTests
{
    private readonly VideoSongFormatProvider _provider = new();

    [Theory]
    [InlineData("/music/song.mp4")]
    [InlineData("/music/song.MP4")]
    public void CanHandleRecognizesMp4Files(string path)
    {
        Assert.True(_provider.CanHandle(CreateCandidate(path)));
    }

    [Theory]
    [InlineData("/music/song.mkv")]
    [InlineData("/music/song.kfn")]
    [InlineData("/music/song")]
    public void CanHandleRejectsOtherFormats(string path)
    {
        Assert.False(_provider.CanHandle(CreateCandidate(path)));
    }

    [Fact]
    public async Task ReadMetadataExtractsArtistAndTitleFromFileName()
    {
        var metadata = await _provider.ReadMetadataAsync(
            CreateCandidate("/music/Кино - Группа крови.mp4"));

        Assert.Equal("Кино", metadata.Artist);
        Assert.Equal("Группа крови", metadata.Title);
        Assert.Null(metadata.Duration);
        Assert.Equal(
            SongCapabilities.Audio | SongCapabilities.Video,
            metadata.Capabilities);
    }

    [Fact]
    public async Task ReadMetadataKeepsAdditionalSeparatorsInTitle()
    {
        var metadata = await _provider.ReadMetadataAsync(
            CreateCandidate("/music/Artist - Song - Live.mp4"));

        Assert.Equal("Artist", metadata.Artist);
        Assert.Equal("Song - Live", metadata.Title);
    }

    [Fact]
    public async Task ReadMetadataUsesWholeFileNameWhenArtistIsMissing()
    {
        var metadata = await _provider.ReadMetadataAsync(
            CreateCandidate("/music/Just a song.mp4"));

        Assert.Null(metadata.Artist);
        Assert.Equal("Just a song", metadata.Title);
    }

    [Fact]
    public async Task ReadMetadataHonorsCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _provider.ReadMetadataAsync(
                CreateCandidate("/music/Artist - Song.mp4"),
                cancellationSource.Token));
    }

    private static FileCandidate CreateCandidate(string path) =>
        new(path, size: 1, lastWriteTime: DateTimeOffset.UnixEpoch);
}
