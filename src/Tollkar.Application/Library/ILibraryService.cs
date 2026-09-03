using Tollkar.Application.Library.Indexing;
using Tollkar.Application.Library.Models;
using Tollkar.Core.Songs;

namespace Tollkar.Application.Library;

public interface ILibraryService
{
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    ValueTask<LibraryRootSummary> AddRootAsync(
        string path,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<LibraryRootSummary>> GetRootsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<LibrarySong>> SearchSongsAsync(
        LibrarySearchQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<LibrarySongCounts> GetSongCountsAsync(
        string? text = null,
        CancellationToken cancellationToken = default);

    ValueTask<Song?> GetSongAsync(Guid songId, CancellationToken cancellationToken = default);

    ValueTask IncrementPlayCountAsync(Guid songId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<LibraryIndexProgress> RefreshRootAsync(
        Guid rootId,
        CancellationToken cancellationToken = default);
}
