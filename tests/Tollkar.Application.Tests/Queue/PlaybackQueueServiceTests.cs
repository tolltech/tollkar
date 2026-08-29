using Tollkar.Application.Queue;
using Tollkar.Application.Queue.Models;
using Tollkar.Application.Queue.Persistence;

namespace Tollkar.Application.Tests.Queue;

public sealed class PlaybackQueueServiceTests
{
    [Fact]
    public async Task RejectsEmptyIdentifiers()
    {
        var service = new PlaybackQueueService(new StubRepository());

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(Guid.Empty).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => service.RemoveAsync(Guid.Empty).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => service.MoveByAsync(Guid.Empty, 1).AsTask());
    }

    private sealed class StubRepository : IPlaybackQueueRepository
    {
        public ValueTask<IReadOnlyList<PlaybackQueueItem>> GetItemsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PlaybackQueueItem>>([]);

        public ValueTask AddAsync(Guid queueItemId, Guid songId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveAsync(Guid queueItemId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask MoveByAsync(Guid queueItemId, int offset, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
