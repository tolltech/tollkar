using Tollkar.Core.Songs;

namespace Tollkar.Application.Queue.Models;

public sealed record PlaybackQueueItem(
    Guid Id,
    Guid SongId,
    string Title,
    string? Artist,
    SongCapabilities Capabilities,
    int Position,
    string UserId);
