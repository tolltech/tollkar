using Tollkar.Application.Queue.Models;
using Tollkar.Application.Queue.Persistence;

namespace Tollkar.Application.Queue;

internal sealed class PlaybackQueueService(
    IPlaybackQueueRepository repository,
    Func<CancellationToken, ValueTask>? initialize = null) : IPlaybackQueueService
{
    private readonly IPlaybackQueueRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly Func<CancellationToken, ValueTask> _initialize =
        initialize ?? (_ => ValueTask.CompletedTask);

    public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
        _initialize(cancellationToken);

    public ValueTask<IReadOnlyList<PlaybackQueueItem>> GetItemsAsync(
        CancellationToken cancellationToken = default) =>
        _repository.GetItemsAsync(cancellationToken);

    public ValueTask AddAsync(Guid songId, CancellationToken cancellationToken = default)
    {
        EnsureNotEmpty(songId, nameof(songId));
        return _repository.AddAsync(Guid.NewGuid(), songId, cancellationToken);
    }

    public ValueTask RemoveAsync(Guid queueItemId, CancellationToken cancellationToken = default)
    {
        EnsureNotEmpty(queueItemId, nameof(queueItemId));
        return _repository.RemoveAsync(queueItemId, cancellationToken);
    }

    public ValueTask MoveByAsync(
        Guid queueItemId,
        int offset,
        CancellationToken cancellationToken = default)
    {
        EnsureNotEmpty(queueItemId, nameof(queueItemId));
        if (offset == 0)
        {
            return ValueTask.CompletedTask;
        }

        return _repository.MoveByAsync(queueItemId, offset, cancellationToken);
    }

    private static void EnsureNotEmpty(Guid id, string parameterName)
    {
        if (id == Guid.Empty) throw new ArgumentException("ID cannot be empty.", parameterName);
    }
}
