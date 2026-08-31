using Microsoft.AspNetCore.SignalR;
using Tollkar.Application.Queue;
using Tollkar.Application.Queue.Models;
using Tollkar.Web.Realtime;
using Vostok.Logging.Abstractions;

namespace Tollkar.Web.Tests;

public sealed class QueuePublicationTests
{
    [Fact]
    public async Task SnapshotFailureDoesNotReportCommittedMutationAsFailed()
    {
        using var coordinator = new QueueStateCoordinator(new TestHubContext(new TestClient()),
            new SilentLog());
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
            new SilentLog());
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

    [Fact]
    public async Task PlaybackTimelineRestoresAndRejectsDuplicateTransitions()
    {
        var clock = new PlaybackClock();
        using var coordinator = new QueueStateCoordinator(new TestHubContext(new TestClient()),
            new SilentLog(), clock);
        var queue = new TestQueue();
        var service = new SynchronizedPlaybackQueue("alice", queue, coordinator);
        for (var i = 0; i < 3; i++) await service.AddAsync(Guid.NewGuid());
        await service.PlayNowAsync(queue.Items[0].Id);
        clock.Advance(12);
        var playing = (await service.GetSnapshotAsync()).Playback!;
        Assert.True(playing.IsPlaying);
        Assert.Equal(12, playing.PositionSeconds);
        await service.ControlAsync(new("pause", playing.Revision));
        clock.Advance(10);
        var paused = (await service.GetSnapshotAsync()).Playback!;
        Assert.False(paused.IsPlaying);
        Assert.Equal(12, paused.PositionSeconds);
        await service.ControlAsync(new("seek", paused.Revision, 42));
        var sought = (await service.GetSnapshotAsync()).Playback!;
        Assert.Equal(42, sought.PositionSeconds);
        Assert.False(sought.IsPlaying);
        await service.ControlAsync(new("play", sought.Revision));
        clock.Advance(3);
        var restored = await new SynchronizedPlaybackQueue("alice", queue, coordinator).GetSnapshotAsync();
        Assert.Equal(45, restored.Playback!.PositionSeconds);
        var ended = new PlaybackCommand("ended", restored.Playback.Revision);
        await Task.WhenAll(service.ControlAsync(ended).AsTask(), service.ControlAsync(ended).AsTask());
        var next = await service.GetSnapshotAsync();
        Assert.Equal(queue.Items[1].Id, next.CurrentItemId);
        Assert.Equal(0, next.Playback!.PositionSeconds);
        await service.ControlAsync(new("seek", restored.Playback.Revision, 500));
        Assert.Equal(next.Playback, (await service.GetSnapshotAsync()).Playback);
        await service.ControlAsync(new("next", next.Playback.Revision));
        var last = await service.GetSnapshotAsync();
        await service.ControlAsync(new("ended", last.Playback!.Revision));
        var finished = await service.GetSnapshotAsync();
        Assert.Null(finished.CurrentItemId);
        Assert.Null(finished.Playback);
        Assert.Equal(3, finished.Items.Count);
    }

    private sealed class PlaybackClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestamp;
        public void Advance(int seconds) => timestamp += seconds * 1000;
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
