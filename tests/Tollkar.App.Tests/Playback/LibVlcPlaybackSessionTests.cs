using Tollkar.App.Playback;
using Tollkar.Core.Playback;
using Tollkar.Core.Songs;

namespace Tollkar.App.Tests.Playback;

public sealed class LibVlcPlaybackSessionTests
{
    [Fact]
    public async Task PlayPauseResumeAndTimeEventsUpdateSession()
    {
        var player = new StubPlayer();
        await using var session = CreateSession(player);

        await session.PlayAsync();
        player.RaisePlaying();
        player.RaiseTimeChanged(1_250);
        await session.PauseAsync();
        player.RaisePaused();
        await session.PlayAsync();
        player.RaisePlaying();

        Assert.Equal(PlaybackState.Playing, session.State);
        Assert.Equal(TimeSpan.FromMilliseconds(1_250), session.Position);
        Assert.Equal([true, false], player.PauseRequests);
        Assert.Equal(1, player.PlayRequests);
    }

    [Fact]
    public async Task EndAndFailureEventsUpdateSession()
    {
        var player = new StubPlayer();
        await using var session = CreateSession(player);
        await session.PlayAsync();

        player.RaiseEndReached();
        Assert.Equal(PlaybackState.Ended, session.State);

        player.RaiseEncounteredError();
        Assert.Equal(PlaybackState.Failed, session.State);
    }

    [Fact]
    public async Task DisposeStopsPlaybackAndIgnoresLateEvents()
    {
        var player = new StubPlayer();
        var media = new StubDisposable();
        var session = CreateSession(player, media);
        await session.PlayAsync();
        player.RaisePlaying();

        await session.DisposeAsync();
        player.RaiseEndReached();

        Assert.Equal(PlaybackState.Playing, session.State);
        Assert.True(player.IsStopped);
        Assert.True(player.IsDisposed);
        Assert.True(media.IsDisposed);
    }

    [Fact]
    public async Task DisposeReleasesResourcesWhenStopFails()
    {
        var player = new StubPlayer { StopException = new InvalidOperationException("Stop failed.") };
        var media = new StubDisposable();
        var session = CreateSession(player, media);
        await session.PlayAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());

        Assert.True(player.IsDisposed);
        Assert.True(media.IsDisposed);
        await session.DisposeAsync();
    }

    private static LibVlcPlaybackProvider.LibVlcPlaybackSession CreateSession(
        StubPlayer player,
        IDisposable? media = null) =>
        new(
            new Song(
                Guid.NewGuid(),
                new SongMetadata("Song", "Artist", null, SongCapabilities.Video),
                new SongSource("video", "/music/song.mp4")),
            player,
            media ?? new StubDisposable());

    private sealed class StubPlayer : LibVlcPlaybackProvider.ILibVlcSessionPlayer
    {
        public List<bool> PauseRequests { get; } = [];
        public int PlayRequests { get; private set; }
        public bool IsStopped { get; private set; }
        public bool IsDisposed { get; private set; }
        public Exception? StopException { get; init; }

        public event EventHandler? Playing;
        public event EventHandler? Paused;
        public event EventHandler? Stopped;
        public event EventHandler? EndReached;
        public event EventHandler? EncounteredError;
        public event EventHandler<LibVlcPlaybackProvider.LibVlcTimeChangedEventArgs>? TimeChanged;

        public bool Play()
        {
            PlayRequests++;
            return true;
        }

        public void SetPause(bool pause) => PauseRequests.Add(pause);

        public void Stop()
        {
            IsStopped = true;
            Stopped?.Invoke(this, EventArgs.Empty);
            if (StopException is not null) throw StopException;
        }

        public void Dispose() => IsDisposed = true;

        public void RaisePlaying() => Playing?.Invoke(this, EventArgs.Empty);
        public void RaisePaused() => Paused?.Invoke(this, EventArgs.Empty);
        public void RaiseEndReached() => EndReached?.Invoke(this, EventArgs.Empty);
        public void RaiseEncounteredError() => EncounteredError?.Invoke(this, EventArgs.Empty);
        public void RaiseTimeChanged(long time) =>
            TimeChanged?.Invoke(this, new LibVlcPlaybackProvider.LibVlcTimeChangedEventArgs(time));
    }

    private sealed class StubDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
