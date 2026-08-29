namespace Tollkar.Application.Library.Models;

public sealed record LibraryRootSummary(
    Guid Id,
    string DisplayName,
    int SongCount);
