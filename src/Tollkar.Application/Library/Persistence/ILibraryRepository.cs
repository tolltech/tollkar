using Tollkar.Application.Library.Models;
using Tollkar.Core.Formats;
using Tollkar.Core.Songs;

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

    ValueTask<Song?> GetSongAsync(Guid songId, CancellationToken cancellationToken = default);

    ValueTask IncrementPlayCountAsync(Guid songId, CancellationToken cancellationToken = default);

    ValueTask<IndexedFileRecord?> GetIndexedFileAsync(
        string path,
        CancellationToken cancellationToken = default);

    ValueTask<Guid> UpsertSongAsync(
        Guid rootId,
        FileCandidate file,
        string providerId,
        int providerVersion,
        SongMetadata metadata,
        CancellationToken cancellationToken = default);

    ValueTask MarkFileSeenAsync(
        string path,
        Guid scanId,
        CancellationToken cancellationToken = default);

    ValueTask RemoveFilesNotSeenAsync(
        Guid rootId,
        Guid scanId,
        CancellationToken cancellationToken = default);
}
