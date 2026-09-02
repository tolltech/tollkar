using Tollkar.Core.Formats;
using Tollkar.Core.Formats.Kfn;
using Tollkar.Core.Songs;
using Tollkar.TestSupport;

namespace Tollkar.Core.Tests.Formats.Kfn;

public sealed class KfnSongFormatProviderTests : IDisposable
{
    private static readonly byte[] Mp4Clip = [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p'];
    private static readonly byte[] AviClip = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0];

    private readonly KfnSongFormatProvider _provider = new();
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"tollkar-kfn-provider-tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("/music/song.kfn")]
    [InlineData("/music/song.KFN")]
    public void CanHandleRecognizesKfnFiles(string path)
    {
        Assert.True(_provider.CanHandle(new FileCandidate(path, 1, DateTimeOffset.UnixEpoch)));
    }

    [Theory]
    [InlineData("/music/song.mp4")]
    [InlineData("/music/song")]
    public void CanHandleRejectsOtherFormats(string path)
    {
        Assert.False(_provider.CanHandle(new FileCandidate(path, 1, DateTimeOffset.UnixEpoch)));
    }

    [Fact]
    public async Task ReadMetadataPrefersTheContainerHeader()
    {
        var path = Build("Дороги.kfn", builder => builder
            .WithTitle("Дороги")
            .WithArtist("Кукрыниксы")
            .WithEntry("Дороги.mp3", 2, [1])
            .WithSongDefinition("[General]\nTitle=Другое\n"));

        var metadata = await _provider.ReadMetadataAsync(Candidate(path));

        Assert.Equal("Дороги", metadata.Title);
        Assert.Equal("Кукрыниксы", metadata.Artist);
        Assert.Null(metadata.Duration);
    }

    [Fact]
    public async Task ReadMetadataFallsBackToFileNameAndContainingFolder()
    {
        var path = Build("Вера.kfn", builder => builder
            .WithEntry("Вера.mp3", 2, [1])
            .WithSongDefinition("[General]\nTitle=\nArtist=\n"));

        var metadata = await _provider.ReadMetadataAsync(Candidate(path));

        Assert.Equal("Вера", metadata.Title);
        Assert.Equal(Path.GetFileName(_directory), metadata.Artist);
    }

    [Fact]
    public async Task ReadMetadataFallsBackToTheSongDefinition()
    {
        var path = Build("song.kfn", builder => builder
            .WithEntry("song.mp3", 2, [1])
            .WithSongDefinition("[General]\nTitle=Есенин\nArtist=Кукрыниксы\n"));

        var metadata = await _provider.ReadMetadataAsync(Candidate(path));

        Assert.Equal("Есенин", metadata.Title);
        Assert.Equal("Кукрыниксы", metadata.Artist);
    }

    [Fact]
    public async Task ReadMetadataReportsLyricsAndPlayableBackground()
    {
        var path = Build("Дорогая.kfn", builder => builder
            .WithEntry("Дорогая.mp3", 2, [1])
            .WithEntry("фон.avi", 5, Mp4Clip)
            .WithSongDefinition("""
                [General]
                [Eff1]
                VideoFile=фон.avi
                [Eff2]
                TextCount=1
                Text0=РАЗ
                Sync0=100
                """));

        var metadata = await _provider.ReadMetadataAsync(Candidate(path));

        Assert.Equal(
            SongCapabilities.Audio | SongCapabilities.Video | SongCapabilities.SyncedLyrics,
            metadata.Capabilities);
    }

    [Fact]
    public async Task ReadMetadataIgnoresBackgroundsBrowsersCannotPlay()
    {
        var path = Build("Колдовство.kfn", builder => builder
            .WithEntry("Колдовство.mp3", 2, [1])
            .WithEntry("фон.avi", 5, AviClip)
            .WithSongDefinition("""
                [General]
                [Eff1]
                VideoFile=фон.avi
                """));

        var metadata = await _provider.ReadMetadataAsync(Candidate(path));

        Assert.Equal(SongCapabilities.Audio, metadata.Capabilities);
    }

    [Fact]
    public async Task ReadMetadataHonorsCancellation()
    {
        var path = Build("song.kfn", builder => builder.WithSongDefinition("[General]\n"));
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _provider.ReadMetadataAsync(Candidate(path), cancellationSource.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string Build(string fileName, Func<KfnFileBuilder, KfnFileBuilder> configure)
    {
        Directory.CreateDirectory(_directory);
        return configure(new KfnFileBuilder()).WriteTo(Path.Combine(_directory, fileName));
    }

    private static FileCandidate Candidate(string path) =>
        new(path, new FileInfo(path).Length, DateTimeOffset.UnixEpoch);
}
