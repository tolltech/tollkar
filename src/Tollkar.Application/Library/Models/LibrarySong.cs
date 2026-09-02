using Tollkar.Core.Songs;

namespace Tollkar.Application.Library.Models;

public sealed record LibrarySong(
    Guid Id,
    string Title,
    string? Artist,
    TimeSpan? Duration,
    SongCapabilities Capabilities,
    string? Folder);
