namespace Tollkar.Application.Library.Indexing;

public sealed record LibraryIndexProgress(
    Guid RootId,
    int DiscoveredFiles,
    int IndexedSongs,
    int FailedFiles,
    bool IsCompleted)
{
    public int UnchangedFiles { get; init; }

    public int IgnoredFiles { get; init; }
}
