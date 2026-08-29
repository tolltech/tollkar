namespace Tollkar.Application.Queue.Models;

public sealed record PlaybackQueueItem(
    Guid Id,
    Guid SongId,
    string Title,
    string? Artist,
    int Position);
