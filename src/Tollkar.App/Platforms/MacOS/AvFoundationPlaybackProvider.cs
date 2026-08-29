using AVFoundation;
using Avalonia.Threading;
using CoreFoundation;
using CoreMedia;
using Foundation;
using Tollkar.Core.Playback;
using Tollkar.Core.Songs;

namespace Tollkar.App.Platforms.MacOS;

internal sealed class AvFoundationPlaybackProvider(MacVideoHost videoHost) : ISongPlaybackProvider
{
    public string FormatProviderId => "video";

    public async ValueTask<ISongPlaybackSession> OpenAsync(
        Song song,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (!File.Exists(song.Source.FilePath))
        {
            throw new FileNotFoundException("Video file was not found.", song.Source.FilePath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        AVPlayerItem? item = null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            item = AVPlayerItem.FromUrl(NSUrl.FromFilename(song.Source.FilePath));
            videoHost.Player.ReplaceCurrentItemWithPlayerItem(item);
        });
        if (cancellationToken.IsCancellationRequested)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(videoHost.Player.CurrentItem, item))
                {
                    videoHost.Player.ReplaceCurrentItemWithPlayerItem(null);
                }
                item?.Dispose();
            });
            cancellationToken.ThrowIfCancellationRequested();
        }
        return new AvFoundationPlaybackSession(song, videoHost.Player, item!);
    }

    private sealed class AvFoundationPlaybackSession : ISongPlaybackSession
    {
        private readonly AVPlayer _player;
        private readonly AVPlayerItem _item;
        private readonly NSObject _timeObserver;
        private readonly NSObject _endedObserver;
        private readonly NSObject _failedObserver;
        private PlaybackState _state;
        private bool _isDisposed;

        public AvFoundationPlaybackSession(Song song, AVPlayer player, AVPlayerItem item)
        {
            Song = song;
            _player = player;
            _item = item;
            _timeObserver = player.AddPeriodicTimeObserver(
                CMTime.FromSeconds(0.25, 600),
                DispatchQueue.MainQueue,
                _ => PositionChanged?.Invoke(this, EventArgs.Empty));
            _endedObserver = AVPlayerItem.Notifications.ObserveDidPlayToEndTime(
                item,
                (_, _) => SetState(PlaybackState.Ended));
            _failedObserver = AVPlayerItem.Notifications.ObserveItemFailedToPlayToEndTime(
                item,
                (_, _) => SetState(PlaybackState.Failed));
        }

        public Song Song { get; }

        public PlaybackState State => _state;

        public TimeSpan Position => TimeSpan.FromSeconds(
            double.IsFinite(_player.CurrentTime.Seconds) ? _player.CurrentTime.Seconds : 0);

        public event EventHandler? StateChanged;

        public event EventHandler? PositionChanged;

        public async ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(_player.Play);
            SetState(PlaybackState.Playing);
        }

        public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(_player.Pause);
            SetState(PlaybackState.Paused);
        }

        public async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _player.Pause();
                _player.Seek(CMTime.Zero);
            });
            SetState(PlaybackState.Stopped);
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _endedObserver.Dispose();
            _failedObserver.Dispose();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _player.RemoveTimeObserver(_timeObserver);
                if (ReferenceEquals(_player.CurrentItem, _item))
                {
                    _player.Pause();
                    _player.ReplaceCurrentItemWithPlayerItem(null);
                }
                _item.Dispose();
            });
        }

        private void SetState(PlaybackState state)
        {
            if (_isDisposed || _state == state) return;
            _state = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
