using Tollkar.Application.Library.Indexing;
using Tollkar.Application.Library.Models;

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

    IAsyncEnumerable<LibraryIndexProgress> RefreshRootAsync(
        Guid rootId,
        CancellationToken cancellationToken = default);
}
