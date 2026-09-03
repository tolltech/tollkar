using System.Runtime.CompilerServices;
using Tollkar.Application.Library.Indexing;
using Tollkar.Application.Library.Models;
using Tollkar.Application.Library.Persistence;
using Tollkar.Core.Songs;

namespace Tollkar.Application.Library;

internal sealed class LibraryService(
    ILibraryRepository repository,
    ILibraryScanner scanner) : ILibraryService
{
    private readonly ILibraryRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ILibraryScanner _scanner =
        scanner ?? throw new ArgumentNullException(nameof(scanner));

    public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
        _repository.InitializeAsync(cancellationToken);

    public async ValueTask<LibraryRootSummary> AddRootAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Library root path cannot be empty.", nameof(path));
        }

        var root = await _repository.AddRootAsync(path, cancellationToken);
        return ToSummary(root);
    }

    public async ValueTask<IReadOnlyList<LibraryRootSummary>> GetRootsAsync(
        CancellationToken cancellationToken = default)
    {
        var roots = await _repository.GetRootsAsync(cancellationToken);
        return roots.Select(ToSummary).ToArray();
    }

    public ValueTask<IReadOnlyList<LibrarySong>> SearchSongsAsync(
        LibrarySearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validatedQuery = query with { Limit = query.ValidatedLimit };
        return _repository.SearchSongsAsync(validatedQuery, cancellationToken);
    }

    public ValueTask<Song?> GetSongAsync(
        Guid songId,
        CancellationToken cancellationToken = default)
    {
        if (songId == Guid.Empty)
        {
            throw new ArgumentException("Song ID cannot be empty.", nameof(songId));
        }

        return _repository.GetSongAsync(songId, cancellationToken);
    }

    public ValueTask IncrementPlayCountAsync(
        Guid songId,
        CancellationToken cancellationToken = default)
    {
        if (songId == Guid.Empty)
        {
            throw new ArgumentException("Song ID cannot be empty.", nameof(songId));
        }

        return _repository.IncrementPlayCountAsync(songId, cancellationToken);
    }

    public async IAsyncEnumerable<LibraryIndexProgress> RefreshRootAsync(
        Guid rootId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (rootId == Guid.Empty)
        {
            throw new ArgumentException("Library root ID cannot be empty.", nameof(rootId));
        }

        await foreach (var progress in _scanner
            .RefreshAsync(rootId, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            yield return progress;
        }
    }

    private static LibraryRootSummary ToSummary(LibraryRootRecord root) =>
        new(root.Id, root.DisplayName, root.SongCount);
}
