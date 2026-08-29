namespace Tollkar.Application.Library.Persistence;

internal sealed record LibraryRootRecord(
    Guid Id,
    string Path,
    string DisplayName,
    int SongCount);
