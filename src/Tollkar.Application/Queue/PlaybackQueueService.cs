using Tollkar.Application.Queue.Models;
using Tollkar.Application.Queue.Persistence;

namespace Tollkar.Application.Queue;

internal sealed class PlaybackQueueService(
    IPlaybackQueueRepository repository,
    string userId,
    Func<CancellationToken, ValueTask>? initialize = null) : IPlaybackQueueService
{
    private readonly string _userId = !string.IsNullOrWhiteSpace(userId)
        ? userId : throw new ArgumentException("User ID cannot be empty.", nameof(userId));
    private readonly IPlaybackQueueRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly Func<CancellationToken, ValueTask> _initialize =
        initialize ?? (_ => ValueTask.CompletedTask);

    public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
        _initialize(cancellationToken);

    public ValueTask<IReadOnlyList<PlaybackQueueItem>> GetItemsAsync(
        CancellationToken cancellationToken = default) =>
        _repository.GetItemsAsync(_userId, cancellationToken);

    public ValueTask AddAsync(Guid songId, CancellationToken cancellationToken = default)
    {
        EnsureNotEmpty(songId, nameof(songId));
        return _repository.AddAsync(_userId, Guid.NewGuid(), songId, cancellationToken);
    }

    public ValueTask RemoveAsync(Guid queueItemId, CancellationToken cancellationToken = default)
    {
        EnsureNotEmpty(queueItemId, nameof(queueItemId));
        return _repository.RemoveAsync(_userId, queueItemId, cancellationToken);
    }

    public ValueTask RemoveAllExceptAsync(
        Guid? retainedQueueItemId,
        CancellationToken cancellationToken = default)
    {
        if (retainedQueueItemId == Guid.Empty)
        {
            throw new ArgumentException("ID cannot be empty.", nameof(retainedQueueItemId));
        }

        return _repository.RemoveAllExceptAsync(_userId, retainedQueueItemId, cancellationToken);
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

        return _repository.MoveByAsync(_userId, queueItemId, offset, cancellationToken);
    }

    private static void EnsureNotEmpty(Guid id, string parameterName)
    {
        if (id == Guid.Empty) throw new ArgumentException("ID cannot be empty.", parameterName);
    }
}
