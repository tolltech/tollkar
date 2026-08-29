namespace Tollkar.Application.Library.Indexing;

internal interface ILibraryScanner
{
    IAsyncEnumerable<LibraryIndexProgress> RefreshAsync(
        Guid rootId,
        CancellationToken cancellationToken = default);
}
