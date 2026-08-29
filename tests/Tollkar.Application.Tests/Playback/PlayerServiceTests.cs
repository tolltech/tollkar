using Tollkar.Application.Library;
using Tollkar.Application.Library.Indexing;
using Tollkar.Application.Library.Models;
using Tollkar.Application.Playback;
using Tollkar.Application.Playback.Models;
using Tollkar.Core.Playback;
using Tollkar.Core.Songs;
using System.Runtime.CompilerServices;

namespace Tollkar.Application.Tests.Playback;

public sealed class PlayerServiceTests
{
    [Fact]
    public async Task PlaysAndTogglesSessionResolvedByFormatProvider()
    {
        var song = CreateSong();
        var session = new StubSession(song);
        var service = new PlayerService(
            new StubLibrary(song),
            new SongPlaybackProviderRegistry([new StubProvider(session)]));

        await service.PlayAsync(song.Id);
        await service.TogglePauseAsync();
        await service.TogglePauseAsync();
        session.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(PlaybackState.Playing, service.Snapshot.State);
        Assert.Equal(song.Id, service.Snapshot.SongId);
        Assert.Equal(TimeSpan.FromSeconds(5), service.Snapshot.Position);
        Assert.Equal(2, session.PlayCount);
        Assert.Equal(1, session.PauseCount);
        await service.DisposeAsync();
        Assert.True(session.IsDisposed);
        Assert.Equal(PlayerSnapshot.Empty, service.Snapshot);
    }

    [Fact]
    public async Task RejectsSongWithoutPlaybackProvider()
    {
        var song = CreateSong();
        var service = new PlayerService(
            new StubLibrary(song),
            new SongPlaybackProviderRegistry([]));

        await Assert.ThrowsAsync<NotSupportedException>(() => service.PlayAsync(song.Id).AsTask());
        await service.DisposeAsync();
    }

    [Fact]
    public async Task ClearsAndDisposesSessionWhenPlayFails()
    {
        var song = CreateSong();
        var session = new StubSession(song) { PlayException = new InvalidOperationException("play failed") };
        var service = new PlayerService(
            new StubLibrary(song),
            new SongPlaybackProviderRegistry([new StubProvider(session)]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlayAsync(song.Id).AsTask());

        Assert.Equal(PlayerSnapshot.Empty, service.Snapshot);
        Assert.True(session.IsDisposed);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task ReportsPlaybackAndCleanupFailuresAndStillClearsSnapshot()
    {
        var song = CreateSong();
        var session = new StubSession(song)
        {
            PlayException = new InvalidOperationException("play failed"),
            DisposeException = new IOException("dispose failed")
        };
        var service = new PlayerService(
            new StubLibrary(song),
            new SongPlaybackProviderRegistry([new StubProvider(session)]));

        var exception = await Assert.ThrowsAsync<AggregateException>(() => service.PlayAsync(song.Id).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Equal(PlayerSnapshot.Empty, service.Snapshot);
        Assert.True(session.IsDisposed);
        await service.DisposeAsync();
    }

    private static Song CreateSong() => new(
        Guid.NewGuid(),
        new SongMetadata("Song", "Artist", null, SongCapabilities.Video),
        new SongSource("video", "/media/song.mp4"));

    private sealed class StubProvider(StubSession session) : ISongPlaybackProvider
    {
        public string FormatProviderId => "video";

        public ValueTask<ISongPlaybackSession> OpenAsync(Song song, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ISongPlaybackSession>(session);
    }

    private sealed class StubSession(Song song) : ISongPlaybackSession
    {
        public Song Song { get; } = song;
        public PlaybackState State { get; private set; }
        public TimeSpan Position { get; private set; }
        public int PlayCount { get; private set; }
        public int PauseCount { get; private set; }
        public bool IsDisposed { get; private set; }
        public Exception? PlayException { get; init; }
        public Exception? DisposeException { get; init; }
        public event EventHandler? StateChanged;
        public event EventHandler? PositionChanged;

        public void Advance(TimeSpan position)
        {
            Position = position;
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            if (PlayException is not null) throw PlayException;
            PlayCount++;
            State = PlaybackState.Playing;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return ValueTask.CompletedTask;
        }

        public ValueTask PauseAsync(CancellationToken cancellationToken = default)
        {
            PauseCount++;
            State = PlaybackState.Paused;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            State = PlaybackState.Stopped;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            if (DisposeException is not null) throw DisposeException;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubLibrary(Song song) : ILibraryService
    {
        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<LibraryRootSummary> AddRootAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<LibraryRootSummary>> GetRootsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<LibrarySong>> SearchSongsAsync(LibrarySearchQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<Song?> GetSongAsync(Guid songId, CancellationToken cancellationToken = default) => ValueTask.FromResult(songId == song.Id ? song : null);
        public async IAsyncEnumerable<LibraryIndexProgress> RefreshRootAsync(
            Guid rootId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }
}
