using System.Runtime.CompilerServices;
using Tollkar.Application.Library;
using Tollkar.Application.Library.Indexing;
using Tollkar.Application.Library.Models;
using Tollkar.Core.Songs;

namespace Tollkar.Web.Realtime;

public sealed class SynchronizedLibrary(ILibraryService library, QueueStateCoordinator coordinator) : ILibraryService
{
    public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
        library.InitializeAsync(cancellationToken);

    public ValueTask<LibraryRootSummary> AddRootAsync(string path, CancellationToken cancellationToken = default) =>
        library.AddRootAsync(path, cancellationToken);

    public ValueTask<IReadOnlyList<LibraryRootSummary>> GetRootsAsync(CancellationToken cancellationToken = default) =>
        library.GetRootsAsync(cancellationToken);

    public ValueTask<IReadOnlyList<LibrarySong>> SearchSongsAsync(LibrarySearchQuery query,
        CancellationToken cancellationToken = default) => library.SearchSongsAsync(query, cancellationToken);

    public ValueTask<Song?> GetSongAsync(Guid songId, CancellationToken cancellationToken = default) =>
        library.GetSongAsync(songId, cancellationToken);

    public ValueTask IncrementPlayCountAsync(Guid songId, CancellationToken cancellationToken = default) =>
        library.IncrementPlayCountAsync(songId, cancellationToken);

    public async IAsyncEnumerable<LibraryIndexProgress> RefreshRootAsync(Guid rootId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var progress in library.RefreshRootAsync(rootId, cancellationToken))
                yield return progress;
        }
        finally
        {
            // Even an interrupted scan may have committed metadata or cascading queue deletions.
            await coordinator.InvalidateLibraryAsync();
        }
    }
}
