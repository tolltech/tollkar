using System.Runtime.CompilerServices;
using Tollkar.Application.Library;
using Tollkar.Application.Library.Indexing;
using Tollkar.Application.Library.Models;
using Tollkar.Application.Library.Persistence;
using Tollkar.Core.Formats;
using Tollkar.Core.Songs;

namespace Tollkar.Application.Tests.Library;

public sealed class LibraryServiceTests
{
    [Fact]
    public async Task AddRootReturnsSummaryWithoutLocalPath()
    {
        var repository = new StubRepository
        {
            AddedRoot = new LibraryRootRecord(
                Guid.NewGuid(),
                "/private/karaoke",
                "karaoke",
                SongCount: 12)
        };
        var service = new LibraryService(repository, new StubScanner());

        var summary = await service.AddRootAsync("/private/karaoke");

        Assert.Equal(repository.AddedRoot.Id, summary.Id);
        Assert.Equal("karaoke", summary.DisplayName);
        Assert.Equal(12, summary.SongCount);
        Assert.DoesNotContain(
            typeof(LibraryRootSummary).GetProperties(),
            property => property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1000, LibrarySearchQuery.MaximumLimit)]
    public async Task SearchSongsClampsRequestedLimit(int requested, int expected)
    {
        var repository = new StubRepository();
        var service = new LibraryService(repository, new StubScanner());

        await service.SearchSongsAsync(new LibrarySearchQuery(Limit: requested));

        Assert.Equal(expected, repository.LastSearchQuery?.Limit);
    }

    [Fact]
    public async Task RefreshRootRejectsEmptyId()
    {
        var service = new LibraryService(new StubRepository(), new StubScanner());

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in service.RefreshRootAsync(Guid.Empty))
            {
            }
        });
    }

    [Fact]
    public async Task RefreshRootForwardsEnumerationCancellation()
    {
        var scanner = new StubScanner();
        var service = new LibraryService(new StubRepository(), scanner);
        using var cancellationSource = new CancellationTokenSource();

        await foreach (var _ in service
            .RefreshRootAsync(Guid.NewGuid())
            .WithCancellation(cancellationSource.Token))
        {
        }

        Assert.Equal(cancellationSource.Token, scanner.LastCancellationToken);
    }

    private sealed class StubRepository : ILibraryRepository
    {
        public LibraryRootRecord AddedRoot { get; init; } =
            new(Guid.NewGuid(), "/music", "music", SongCount: 0);

        public LibrarySearchQuery? LastSearchQuery { get; private set; }

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<LibraryRootRecord> AddRootAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AddedRoot);

        public ValueTask<LibraryRootRecord?> GetRootAsync(
            Guid rootId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<LibraryRootRecord?>(AddedRoot);

        public ValueTask<IReadOnlyList<LibraryRootRecord>> GetRootsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<LibraryRootRecord>>([AddedRoot]);

        public ValueTask<IReadOnlyList<LibrarySong>> SearchSongsAsync(
            LibrarySearchQuery query,
            CancellationToken cancellationToken = default)
        {
            LastSearchQuery = query;
            return ValueTask.FromResult<IReadOnlyList<LibrarySong>>([]);
        }

        public ValueTask<LibrarySongCounts> GetSongCountsAsync(string? text = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new LibrarySongCounts(0, 0));

        public ValueTask<Tollkar.Core.Songs.Song?> GetSongAsync(
            Guid songId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Tollkar.Core.Songs.Song?>(null);

        public ValueTask IncrementPlayCountAsync(
            Guid songId,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<IndexedFileRecord?> GetIndexedFileAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IndexedFileRecord?>(null);

        public ValueTask<Guid> UpsertSongAsync(
            Guid rootId,
            FileCandidate file,
            string providerId,
            int providerVersion,
            SongMetadata metadata,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Guid.NewGuid());

        public ValueTask MarkFileSeenAsync(
            string path,
            Guid scanId,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveFilesNotSeenAsync(
            Guid rootId,
            Guid scanId,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubScanner : ILibraryScanner
    {
        public CancellationToken LastCancellationToken { get; private set; }

        public async IAsyncEnumerable<LibraryIndexProgress> RefreshAsync(
            Guid rootId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            await Task.CompletedTask;
            yield break;
        }
    }
}
