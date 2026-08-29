using Tollkar.Application.Queue.Models;

namespace Tollkar.Application.Queue.Persistence;

internal interface IPlaybackQueueRepository
{
    ValueTask<IReadOnlyList<PlaybackQueueItem>> GetItemsAsync(
        CancellationToken cancellationToken = default);

    ValueTask AddAsync(
        Guid queueItemId,
        Guid songId,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(Guid queueItemId, CancellationToken cancellationToken = default);

    ValueTask MoveByAsync(
        Guid queueItemId,
        int offset,
        CancellationToken cancellationToken = default);
}
