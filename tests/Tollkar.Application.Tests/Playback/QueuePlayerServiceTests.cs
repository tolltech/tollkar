using Tollkar.Application.Playback;
using Tollkar.Application.Playback.Models;
using Tollkar.Application.Queue;
using Tollkar.Application.Queue.Models;
using Tollkar.Core.Playback;
using Tollkar.Core.Songs;

namespace Tollkar.Application.Tests.Playback;

public sealed class QueuePlayerServiceTests
{
    [Fact]
    public async Task TogglePauseStartsFirstItemWhenNothingIsPlaying()
    {
        var first = CreateQueueItem(0);
        var queue = new StubQueue([first]);
        var player = new StubPlayer();
        await using var service = new QueuePlayerService(queue, player);

        await service.TogglePauseAsync();

        Assert.Equal([first.SongId], player.PlayedSongIds);
        Assert.Equal([first.Id], queue.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task TogglePauseDelegatesWhenSongIsAlreadyActive()
    {
        var first = CreateQueueItem(0);
        var queue = new StubQueue([first]);
        var player = new StubPlayer();
        await using var service = new QueuePlayerService(queue, player);
        await service.PlayQueueItemAsync(first.Id);

        await service.TogglePauseAsync();

        Assert.Equal(1, player.TogglePauseCount);
        Assert.Equal([first.SongId], player.PlayedSongIds);
        Assert.Empty(queue.RemovedItemIds);
    }

    [Fact]
    public async Task ImmediatelyEndedSongAdvancesAfterStartupCompletes()
    {
        var first = CreateQueueItem(0);
        var second = CreateQueueItem(1);
        var queue = new StubQueue([first, second]);
        var player = new StubPlayer { EndFirstPlayImmediately = true };
        await using var service = new QueuePlayerService(queue, player);

        await service.TogglePauseAsync();
        await player.SecondSongPlayed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([first.Id], queue.RemovedItemIds);
        Assert.Equal([first.SongId, second.SongId], player.PlayedSongIds);
    }

    [Fact]
    public async Task NextStartsFirstItemWhenNothingIsPlaying()
    {
        var first = CreateQueueItem(0);
        var second = CreateQueueItem(1);
        var queue = new StubQueue([first, second]);
        var player = new StubPlayer();
        await using var service = new QueuePlayerService(queue, player);

        await service.NextAsync();

        Assert.Equal([first.SongId], player.PlayedSongIds);
        Assert.Equal([first.Id, second.Id], queue.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task EndedNotificationsAdvanceQueueOnlyOnce()
    {
        var first = CreateQueueItem(0);
        var second = CreateQueueItem(1);
        var queue = new StubQueue([first, second]);
        var player = new StubPlayer();
        await using var service = new QueuePlayerService(queue, player);
        await service.PlayQueueItemAsync(first.Id);

        player.SetState(PlaybackState.Ended);
        player.NotifySnapshotChanged();
        player.NotifySnapshotChanged();
        await player.SecondSongPlayed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([first.Id], queue.RemovedItemIds);
        Assert.Equal([first.SongId, second.SongId], player.PlayedSongIds);
    }

    [Fact]
    public async Task EndedNotificationDuringNextSongStartupDoesNotAdvanceAgain()
    {
        var first = CreateQueueItem(0);
        var second = CreateQueueItem(1);
        var third = CreateQueueItem(2);
        var queue = new StubQueue([first, second, third]);
        var player = new StubPlayer { PauseSecondPlay = true };
        await using var service = new QueuePlayerService(queue, player);
        await service.PlayQueueItemAsync(first.Id);

        player.SetState(PlaybackState.Ended);
        player.NotifySnapshotChanged();
        await player.SecondSongStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        player.NotifySnapshotChanged();
        player.AllowSecondPlay.TrySetResult();
        await player.SecondSongPlayed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.NextAsync();

        Assert.Equal([first.Id, second.Id], queue.RemovedItemIds);
        Assert.Equal([first.SongId, second.SongId, third.SongId], player.PlayedSongIds);
    }

    [Fact]
    public async Task NextRemovesCurrentItemAndPlaysFollowingItem()
    {
        var first = CreateQueueItem(0);
        var second = CreateQueueItem(1);
        var third = CreateQueueItem(2);
        var queue = new StubQueue([first, second, third]);
        var player = new StubPlayer();
        await using var service = new QueuePlayerService(queue, player);
        await service.PlayQueueItemAsync(second.Id);

        await service.NextAsync();

        Assert.Equal([second.Id], queue.RemovedItemIds);
        Assert.Equal([second.SongId, third.SongId], player.PlayedSongIds);
    }

    private static PlaybackQueueItem CreateQueueItem(int position) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        $"Song {position}",
        "Artist",
        SongCapabilities.Audio | SongCapabilities.Video,
        position,
        "alice");

    private sealed class StubQueue(IEnumerable<PlaybackQueueItem> items) : IPlaybackQueueService
    {
        private readonly List<PlaybackQueueItem> _items = [.. items];

        public IReadOnlyList<PlaybackQueueItem> Items => _items;

        public List<Guid> RemovedItemIds { get; } = [];

        public TaskCompletionSource ItemRemoved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<PlaybackQueueItem>> GetItemsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PlaybackQueueItem>>([.. _items]);

        public ValueTask AddAsync(Guid songId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask RemoveAsync(Guid queueItemId, CancellationToken cancellationToken = default)
        {
            RemovedItemIds.Add(queueItemId);
            _items.RemoveAll(item => item.Id == queueItemId);
            ItemRemoved.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAllExceptAsync(
            Guid? retainedQueueItemId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask MoveByAsync(
            Guid queueItemId,
            int offset,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubPlayer : IPlayerService
    {
        public PlayerSnapshot Snapshot { get; private set; } = PlayerSnapshot.Empty;

        public List<Guid> PlayedSongIds { get; } = [];

        public TaskCompletionSource SecondSongPlayed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondSongStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowSecondPlay { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool PauseSecondPlay { get; init; }

        public bool EndFirstPlayImmediately { get; init; }

        public int TogglePauseCount { get; private set; }

        public event EventHandler? SnapshotChanged;

        public async ValueTask PlayAsync(Guid songId, CancellationToken cancellationToken = default)
        {
            PlayedSongIds.Add(songId);
            if (PlayedSongIds.Count == 2)
            {
                SecondSongStarted.TrySetResult();
                if (PauseSecondPlay)
                {
                    await AllowSecondPlay.Task.WaitAsync(cancellationToken);
                }
            }
            var state = EndFirstPlayImmediately && PlayedSongIds.Count == 1
                ? PlaybackState.Ended
                : PlaybackState.Playing;
            Snapshot = new(songId, "Song", "Artist", state, TimeSpan.Zero);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            if (PlayedSongIds.Count == 2) SecondSongPlayed.TrySetResult();
        }

        public ValueTask TogglePauseAsync(CancellationToken cancellationToken = default)
        {
            TogglePauseCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            Snapshot = PlayerSnapshot.Empty;
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void SetState(PlaybackState state) => Snapshot = Snapshot with { State = state };

        public void NotifySnapshotChanged() => SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }
}
