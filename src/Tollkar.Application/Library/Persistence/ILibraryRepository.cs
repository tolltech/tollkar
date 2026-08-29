using Tollkar.Application.Library.Models;

namespace Tollkar.Application.Library.Persistence;

internal interface ILibraryRepository
{
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    ValueTask<LibraryRootRecord> AddRootAsync(
        string path,
        CancellationToken cancellationToken = default);

    ValueTask<LibraryRootRecord?> GetRootAsync(
        Guid rootId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<LibraryRootRecord>> GetRootsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<LibrarySong>> SearchSongsAsync(
        LibrarySearchQuery query,
        CancellationToken cancellationToken = default);
}
