using Tollkar.Application.Library;
using Tollkar.Application.Playback.Models;
using Tollkar.Core.Playback;

namespace Tollkar.Application.Playback;

internal sealed class PlayerService(
    ILibraryService library,
    SongPlaybackProviderRegistry providers) : IPlayerService
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _sessionLock = new();
    private ISongPlaybackSession? _session;
    private PlayerSnapshot _snapshot = PlayerSnapshot.Empty;

    public PlayerSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public event EventHandler? SnapshotChanged;

    public async ValueTask PlayAsync(Guid songId, CancellationToken cancellationToken = default)
    {
        if (songId == Guid.Empty) throw new ArgumentException("Song ID cannot be empty.", nameof(songId));
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var song = await library.GetSongAsync(songId, cancellationToken) ??
                throw new KeyNotFoundException($"Song '{songId}' was not found.");
            var provider = providers.FindProvider(song.Source.ProviderId) ??
                throw new NotSupportedException($"No playback provider is registered for '{song.Source.ProviderId}'.");
            try
            {
                await ClearSessionAsync();
            }
            finally
            {
                PublishSnapshot(PlayerSnapshot.Empty);
            }

            var session = await provider.OpenAsync(song, cancellationToken);
            session.StateChanged += Session_OnSessionChanged;
            session.PositionChanged += Session_OnSessionChanged;
            lock (_sessionLock) _session = session;
            try
            {
                await session.PlayAsync(cancellationToken);
                try
                {
                    await library.IncrementPlayCountAsync(song.Id, cancellationToken);
                }
                catch (Exception)
                {
                }
                PublishSessionSnapshot(session);
            }
            catch (Exception playbackException)
            {
                Exception? cleanupException = null;
                try
                {
                    await ClearSessionAsync();
                }
                catch (Exception exception)
                {
                    cleanupException = exception;
                }
                finally
                {
                    PublishSnapshot(PlayerSnapshot.Empty);
                }
                if (cleanupException is not null)
                {
                    throw new AggregateException(playbackException, cleanupException);
                }
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async ValueTask TogglePauseAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (_session is null) return;
            if (_session.State == PlaybackState.Playing)
            {
                await _session.PauseAsync(cancellationToken);
            }
            else
            {
                await _session.PlayAsync(cancellationToken);
            }
            PublishSessionSnapshot(_session);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var session = _session;
            if (session is null) return;
            try
            {
                await session.StopAsync(cancellationToken);
            }
            finally
            {
                try
                {
                    await ClearSessionAsync();
                }
                finally
                {
                    PublishSnapshot(PlayerSnapshot.Empty);
                }
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operationLock.WaitAsync();
        try
        {
            try
            {
                await ClearSessionAsync();
            }
            finally
            {
                PublishSnapshot(PlayerSnapshot.Empty);
            }
        }
        finally
        {
            _operationLock.Release();
            _operationLock.Dispose();
        }
    }

    private void Session_OnSessionChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is ISongPlaybackSession session)
        {
            PublishSessionSnapshot(session);
        }
    }

    private void PublishSessionSnapshot(ISongPlaybackSession session)
    {
        lock (_sessionLock)
        {
            if (!ReferenceEquals(session, _session)) return;
            Volatile.Write(ref _snapshot, new(
                session.Song.Id,
                session.Song.Metadata.Title,
                session.Song.Metadata.Artist,
                session.State,
                session.Position));
        }
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishSnapshot(PlayerSnapshot snapshot)
    {
        lock (_sessionLock) Volatile.Write(ref _snapshot, snapshot);
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private async ValueTask ClearSessionAsync()
    {
        ISongPlaybackSession? session;
        lock (_sessionLock)
        {
            session = _session;
            _session = null;
            if (session is not null)
            {
                session.StateChanged -= Session_OnSessionChanged;
                session.PositionChanged -= Session_OnSessionChanged;
            }
        }
        if (session is null) return;
        await session.DisposeAsync();
    }
}
