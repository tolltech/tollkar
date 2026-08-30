using Tollkar.Application.Queue.Models;

namespace Tollkar.Application.Queue.Persistence;

internal interface IPlaybackQueueRepository
{
    ValueTask<IReadOnlyList<PlaybackQueueItem>> GetItemsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    ValueTask AddAsync(
        string userId,
        Guid queueItemId,
        Guid songId,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string userId, Guid queueItemId, CancellationToken cancellationToken = default);

    ValueTask MoveByAsync(
        string userId,
        Guid queueItemId,
        int offset,
        CancellationToken cancellationToken = default);
}
