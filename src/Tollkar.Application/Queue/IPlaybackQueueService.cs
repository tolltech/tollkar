using Tollkar.Application.Queue.Models;

namespace Tollkar.Application.Queue;

public interface IPlaybackQueueService
{
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<PlaybackQueueItem>> GetItemsAsync(
        CancellationToken cancellationToken = default);

    ValueTask AddAsync(Guid songId, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(Guid queueItemId, CancellationToken cancellationToken = default);

    ValueTask MoveByAsync(
        Guid queueItemId,
        int offset,
        CancellationToken cancellationToken = default);
}
