using Tollkar.Application.Library.Indexing;
using Tollkar.Application.Library.Models;
using Tollkar.Core.Formats;
using Tollkar.Core.Formats.Video;
using Tollkar.Core.Songs;
using Tollkar.Infrastructure.Library;

namespace Tollkar.Infrastructure.Tests.Library;

public sealed class BackgroundLibraryScannerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"tollkar-scanner-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ScanIndexesSupportedFilesAndSkipsThemNextTime()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "Кино - Группа крови.mp4"), [1]);
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "ignored");
        var repository = new SqliteLibraryRepository(Path.Combine(_directory, "library.db"));
        await repository.InitializeAsync();
        var root = await repository.AddRootAsync(_directory);
        var scanner = new BackgroundLibraryScanner(
            repository,
            new SongFormatProviderRegistry([new VideoSongFormatProvider()]),
            workerCount: 2);

        var firstProgress = await CollectAsync(scanner.RefreshAsync(root.Id));
        var secondProgress = await CollectAsync(scanner.RefreshAsync(root.Id));
        var songs = await repository.SearchSongsAsync(new LibrarySearchQuery("кино"));

        Assert.True(firstProgress[^1].IsCompleted);
        Assert.Equal(1, firstProgress[^1].IndexedSongs);
        Assert.Equal(0, firstProgress[^1].FailedFiles);
        Assert.Equal(1, secondProgress[^1].UnchangedFiles);
        Assert.Single(songs);

        File.Delete(Path.Combine(_directory, "Кино - Группа крови.mp4"));
        await CollectAsync(scanner.RefreshAsync(root.Id));

        Assert.Empty(await repository.SearchSongsAsync(new LibrarySearchQuery("кино")));
    }

    [Fact]
    public async Task ScanContinuesAfterMetadataFailure()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "good.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(_directory, "bad.mp4"), [2]);
        var repository = new SqliteLibraryRepository(Path.Combine(_directory, "library.db"));
        await repository.InitializeAsync();
        var root = await repository.AddRootAsync(_directory);
        var scanner = new BackgroundLibraryScanner(
            repository,
            new SongFormatProviderRegistry([new FailingProvider()]),
            workerCount: 2);

        var progress = await CollectAsync(scanner.RefreshAsync(root.Id));

        Assert.Equal(1, progress[^1].IndexedSongs);
        Assert.Equal(1, progress[^1].FailedFiles);
        Assert.True(progress[^1].IsCompleted);
    }

    [Fact]
    public async Task ConcurrentScansOfSameRootAreSerialized()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "song.mp4"), [1]);
        var repository = new SqliteLibraryRepository(Path.Combine(_directory, "library.db"));
        await repository.InitializeAsync();
        var root = await repository.AddRootAsync(_directory);
        var provider = new ConcurrencyTrackingProvider();
        var scanner = new BackgroundLibraryScanner(
            repository,
            new SongFormatProviderRegistry([provider]),
            workerCount: 1);

        await Task.WhenAll(
            CollectAsync(scanner.RefreshAsync(root.Id)),
            CollectAsync(scanner.RefreshAsync(root.Id)));

        Assert.Equal(1, provider.MaximumConcurrency);
    }

    [Fact]
    public async Task MetadataFailurePreservesPreviouslyIndexedSong()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "song.mp4");
        File.WriteAllBytes(path, [1]);
        var repository = new SqliteLibraryRepository(Path.Combine(_directory, "library.db"));
        await repository.InitializeAsync();
        var root = await repository.AddRootAsync(_directory);
        var workingScanner = new BackgroundLibraryScanner(
            repository,
            new SongFormatProviderRegistry([new VideoSongFormatProvider()]));
        await CollectAsync(workingScanner.RefreshAsync(root.Id));
        File.WriteAllBytes(path, [1, 2]);
        var failingScanner = new BackgroundLibraryScanner(
            repository,
            new SongFormatProviderRegistry([new FailingProvider(failAll: true)]));

        await CollectAsync(failingScanner.RefreshAsync(root.Id));

        Assert.Single(await repository.SearchSongsAsync(new LibrarySearchQuery("song")));
    }

    [Fact]
    public async Task MetadataFailuresAreSkippedUntilScannerRestarts()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "bad.mp4");
        File.WriteAllBytes(path, [1]);
        var repository = new SqliteLibraryRepository(Path.Combine(_directory, "library.db"));
        await repository.InitializeAsync();
        var root = await repository.AddRootAsync(_directory);
        var provider = new FailingProvider();
        var scanner = new BackgroundLibraryScanner(
            repository,
            new SongFormatProviderRegistry([provider]));

        var first = await CollectAsync(scanner.RefreshAsync(root.Id));
        var second = await CollectAsync(scanner.RefreshAsync(root.Id));
        var afterRestart = await CollectAsync(new BackgroundLibraryScanner(
            repository,
            new SongFormatProviderRegistry([provider])).RefreshAsync(root.Id));

        Assert.Equal([path], first[^1].FailedFilePaths);
        Assert.Equal(1, first[^1].FailedFiles);
        Assert.Equal(0, second[^1].FailedFiles);
        Assert.Equal(1, second[^1].UnchangedFiles);
        Assert.Empty(second[^1].FailedFilePaths);
        Assert.Equal(1, afterRestart[^1].FailedFiles);
        Assert.Equal(2, provider.ReadAttempts);
    }

    [Fact]
    public async Task DoesNotPublishFailurePathsWhenOneHundredFilesFail()
    {
        Directory.CreateDirectory(_directory);
        for (var index = 0; index < 100; index++)
            File.WriteAllBytes(Path.Combine(_directory, $"bad-{index}.mp4"), [1]);
        var repository = new SqliteLibraryRepository(Path.Combine(_directory, "library.db"));
        await repository.InitializeAsync();
        var root = await repository.AddRootAsync(_directory);
        var scanner = new BackgroundLibraryScanner(
            repository,
            new SongFormatProviderRegistry([new FailingProvider(failAll: true)]));

        var progress = await CollectAsync(scanner.RefreshAsync(root.Id));

        Assert.Equal(100, progress[^1].FailedFiles);
        Assert.Empty(progress[^1].FailedFilePaths);
    }

    private static async Task<List<LibraryIndexProgress>> CollectAsync(
        IAsyncEnumerable<LibraryIndexProgress> source)
    {
        var items = new List<LibraryIndexProgress>();
        await foreach (var item in source) items.Add(item);
        return items;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class FailingProvider(bool failAll = false) : ISongFormatProvider
    {
        public int ReadAttempts { get; private set; }

        public string Id => "test";
        public int Version => 1;
        public int Priority => 0;
        public bool CanHandle(FileCandidate file) => file.Path.EndsWith(".mp4");

        public ValueTask<SongMetadata> ReadMetadataAsync(
            FileCandidate file,
            CancellationToken cancellationToken = default)
        {
            ReadAttempts++;
            return failAll || file.Path.EndsWith("bad.mp4", StringComparison.Ordinal)
                ? throw new InvalidDataException("Broken test file.")
                : ValueTask.FromResult(new SongMetadata(
                    "Good",
                    null,
                    null,
                    SongCapabilities.Video));
        }
    }

    private sealed class ConcurrencyTrackingProvider : ISongFormatProvider
    {
        private int _active;
        private int _maximumConcurrency;

        public string Id => "concurrency-test";
        public int Version => 1;
        public int Priority => 0;
        public int MaximumConcurrency => _maximumConcurrency;
        public bool CanHandle(FileCandidate file) => file.Path.EndsWith(".mp4", StringComparison.Ordinal);

        public async ValueTask<SongMetadata> ReadMetadataAsync(
            FileCandidate file,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            InterlockedExtensions.Max(ref _maximumConcurrency, active);
            try
            {
                await Task.Delay(50, cancellationToken);
                return new("Song", null, null, SongCapabilities.Video);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (value > current)
            {
                var previous = Interlocked.CompareExchange(ref location, value, current);
                if (previous == current) return;
                current = previous;
            }
        }
    }
}
