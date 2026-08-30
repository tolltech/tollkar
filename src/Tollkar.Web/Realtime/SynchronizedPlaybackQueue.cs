using Tollkar.Application.Queue;
using Tollkar.Application.Queue.Models;

namespace Tollkar.Web.Realtime;

public sealed class SynchronizedPlaybackQueue(string userId, IPlaybackQueueService queue,
    QueueStateCoordinator coordinator) : IPlaybackQueueService
{
    public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
        queue.InitializeAsync(cancellationToken);

    public Task<QueueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        coordinator.ReadAsync(userId, queue, cancellationToken);

    public ValueTask PlayNowAsync(Guid queueItemId, CancellationToken cancellationToken = default) =>
        coordinator.PlayNowAsync(userId, queue, queueItemId, cancellationToken);

    public ValueTask ControlAsync(PlaybackCommand command, CancellationToken cancellationToken = default) =>
        coordinator.ControlAsync(userId, queue, command, cancellationToken);

    public async ValueTask<IReadOnlyList<PlaybackQueueItem>> GetItemsAsync(CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).Items;

    public ValueTask AddAsync(Guid songId, CancellationToken cancellationToken = default) =>
        coordinator.MutateAsync(userId, queue, token => queue.AddAsync(songId, token), cancellationToken);

    public ValueTask RemoveAsync(Guid queueItemId, CancellationToken cancellationToken = default) =>
        coordinator.MutateAsync(userId, queue, token => queue.RemoveAsync(queueItemId, token), cancellationToken);

    public ValueTask MoveByAsync(Guid queueItemId, int offset, CancellationToken cancellationToken = default) =>
        coordinator.MutateAsync(userId, queue, token => queue.MoveByAsync(queueItemId, offset, token), cancellationToken);
}
