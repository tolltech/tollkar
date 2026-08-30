using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Tollkar.Application.Queue;
using Tollkar.Application.Queue.Models;
using Tollkar.Web.Realtime;

namespace Tollkar.Web.Tests;

public sealed class QueuePublicationTests
{
    [Fact]
    public async Task SnapshotFailureDoesNotReportCommittedMutationAsFailed()
    {
        using var coordinator = new QueueStateCoordinator(new TestHubContext(new TestClient()),
            NullLogger<QueueStateCoordinator>.Instance);
        var queue = new TestQueue { ReadError = new IOException("Snapshot unavailable") };
        var service = new SynchronizedPlaybackQueue("alice", queue, coordinator);
        await service.AddAsync(Guid.NewGuid());
        Assert.Single(queue.Items);
        queue.ReadError = null;
        var restored = await service.GetSnapshotAsync();
        Assert.Single(restored.Items);
        Assert.True(restored.Version > 0);
    }

    [Fact]
    public async Task BlockedPublicationDoesNotBlockSnapshots()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new TestClient
        {
            Publish = async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        using var coordinator = new QueueStateCoordinator(new TestHubContext(client),
            NullLogger<QueueStateCoordinator>.Instance);
        var service = new SynchronizedPlaybackQueue("alice", new TestQueue(), coordinator);
        var mutation = service.AddAsync(Guid.NewGuid()).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var snapshot = await service.GetSnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Single(snapshot.Items);
            Assert.False(mutation.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await mutation;
    }

    private sealed class TestQueue : IPlaybackQueueService
    {
        public List<PlaybackQueueItem> Items { get; } = [];
        public Exception? ReadError { get; set; }
        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<PlaybackQueueItem>> GetItemsAsync(CancellationToken cancellationToken = default) =>
            ReadError is null ? ValueTask.FromResult<IReadOnlyList<PlaybackQueueItem>>(Items.ToArray())
                : ValueTask.FromException<IReadOnlyList<PlaybackQueueItem>>(ReadError);
        public ValueTask AddAsync(Guid songId, CancellationToken cancellationToken = default)
        {
            Items.Add(new(Guid.NewGuid(), songId, "Song", null, Items.Count, "alice"));
            return ValueTask.CompletedTask;
        }
        public ValueTask RemoveAsync(Guid queueItemId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask MoveByAsync(Guid queueItemId, int offset, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestClient : IClientProxy
    {
        public Func<CancellationToken, Task> Publish { get; init; } = _ => Task.CompletedTask;
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            Publish(cancellationToken);
    }

    private sealed class TestHubContext(IClientProxy client) : IHubContext<KaraokeHub>
    {
        public IHubClients Clients { get; } = new TestHubClients(client);
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class TestHubClients(IClientProxy client) : IHubClients
    {
        public IClientProxy All => client;
        public IClientProxy Group(string groupName) => client;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }
}
