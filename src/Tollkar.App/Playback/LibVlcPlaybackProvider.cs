using System.Runtime.InteropServices;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using Tollkar.Core.Playback;
using Tollkar.Core.Songs;

namespace Tollkar.App.Playback;

internal sealed class LibVlcPlaybackProvider : ISongPlaybackProvider, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly nint _libVlcHandle;
    private readonly nint _libVlcCoreHandle;
    private bool _isDisposed;

    private LibVlcPlaybackProvider(
        string libraryDirectory,
        string pluginsDirectory,
        nint libVlcHandle,
        nint libVlcCoreHandle)
    {
        Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginsDirectory);
        LibVLCSharp.Shared.Core.Initialize(libraryDirectory);
        _libVlc = new LibVLC("--no-video-title-show");
        _mediaPlayer = new MediaPlayer(_libVlc);
        _libVlcHandle = libVlcHandle;
        _libVlcCoreHandle = libVlcCoreHandle;
        VideoView = new VideoView { MediaPlayer = _mediaPlayer };
    }

    public string FormatProviderId => "video";

    public VideoView VideoView { get; }

    public static bool TryCreate(
        out LibVlcPlaybackProvider? provider,
        out string? unavailableMessage)
    {
        provider = null;
        unavailableMessage = null;
        if (!OperatingSystem.IsMacOS())
        {
            unavailableMessage = "Встроенное воспроизведение пока поддерживается только на macOS.";
            return false;
        }

        var runtimes = LibVlcRuntimeLocator.FindAll();
        if (runtimes.Count == 0)
        {
            unavailableMessage =
                "Для встроенного воспроизведения установите VLC: brew install --cask vlc";
            return false;
        }

        Exception? loadException = null;
        foreach (var runtime in runtimes)
        {
            nint coreHandle = 0;
            nint vlcHandle = 0;
            try
            {
                (vlcHandle, coreHandle) = LibVlcNativeLibrary.Load(runtime);
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or BadImageFormatException)
            {
                loadException = exception;
                if (vlcHandle != 0) NativeLibrary.Free(vlcHandle);
                if (coreHandle != 0) NativeLibrary.Free(coreHandle);
                continue;
            }

            try
            {
                provider = new LibVlcPlaybackProvider(
                    runtime.LibraryDirectory,
                    runtime.PluginsDirectory,
                    vlcHandle,
                    coreHandle);
                return true;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or BadImageFormatException or VLCException)
            {
                NativeLibrary.Free(vlcHandle);
                NativeLibrary.Free(coreHandle);
                loadException = exception;
                break;
            }
        }

        unavailableMessage =
            $"Не удалось загрузить LibVLC ({loadException?.Message}). Переустановите VLC: brew install --cask vlc";
        return false;
    }

    public ValueTask<ISongPlaybackSession> OpenAsync(
        Song song,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!File.Exists(song.Source.FilePath))
        {
            throw new FileNotFoundException("Video file was not found.", song.Source.FilePath);
        }

        var media = new Media(_libVlc, new Uri(song.Source.FilePath));
        return ValueTask.FromResult<ISongPlaybackSession>(
            new LibVlcPlaybackSession(
                song,
                new LibVlcSessionPlayer(_mediaPlayer, media),
                media));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        VideoView.MediaPlayer = null;
        _mediaPlayer.Dispose();
        _libVlc.Dispose();
        NativeLibrary.Free(_libVlcHandle);
        NativeLibrary.Free(_libVlcCoreHandle);
    }

    internal sealed class LibVlcPlaybackSession : ISongPlaybackSession
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly ILibVlcSessionPlayer _player;
        private readonly IDisposable _media;
        private PlaybackState _state;
        private TimeSpan _position;
        private bool _hasStarted;
        private bool _isStopping;
        private bool _isDisposed;

        public LibVlcPlaybackSession(
            Song song,
            ILibVlcSessionPlayer player,
            IDisposable media)
        {
            Song = song;
            _player = player;
            _media = media;
            _player.Playing += Player_OnPlaying;
            _player.Paused += Player_OnPaused;
            _player.Stopped += Player_OnStopped;
            _player.EndReached += Player_OnEndReached;
            _player.EncounteredError += Player_OnEncounteredError;
            _player.TimeChanged += Player_OnTimeChanged;
        }

        public Song Song { get; }

        public PlaybackState State
        {
            get
            {
                lock (_sync) return _state;
            }
        }

        public TimeSpan Position
        {
            get
            {
                lock (_sync) return _position;
            }
        }

        public event EventHandler? StateChanged;

        public event EventHandler? PositionChanged;

        public async ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                bool resume;
                lock (_sync)
                {
                    _isStopping = false;
                    resume = _hasStarted;
                    _hasStarted = true;
                }
                var started = resume ? ResumePlayback() : _player.Play();
                if (!started)
                {
                    lock (_sync) _hasStarted = false;
                    SetState(PlaybackState.Failed);
                    throw new InvalidOperationException("LibVLC could not start video playback.");
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                _player.SetPause(true);
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
                ThrowIfDisposed();
                await StopCoreAsync(cancellationToken);
                SetPosition(TimeSpan.Zero);
                SetState(PlaybackState.Stopped);
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
                lock (_sync)
                {
                    if (_isDisposed) return;
                    _isDisposed = true;
                }
                try
                {
                    await StopCoreAsync(CancellationToken.None);
                }
                finally
                {
                    Unsubscribe();
                    try
                    {
                        _player.Dispose();
                    }
                    finally
                    {
                        _media.Dispose();
                    }
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private bool ResumePlayback()
        {
            _player.SetPause(false);
            return true;
        }

        private async Task StopCoreAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _isStopping = true;
                if (!_hasStarted) return;
                _hasStarted = false;
            }
            await Task.Run(_player.Stop, cancellationToken);
        }

        private void Player_OnPlaying(object? sender, EventArgs eventArgs) =>
            SetState(PlaybackState.Playing);

        private void Player_OnPaused(object? sender, EventArgs eventArgs) =>
            SetState(PlaybackState.Paused);

        private void Player_OnStopped(object? sender, EventArgs eventArgs)
        {
            lock (_sync)
            {
                if (_isStopping) return;
            }
            SetState(PlaybackState.Stopped);
        }

        private void Player_OnEndReached(object? sender, EventArgs eventArgs) =>
            SetState(PlaybackState.Ended);

        private void Player_OnEncounteredError(object? sender, EventArgs eventArgs) =>
            SetState(PlaybackState.Failed);

        private void Player_OnTimeChanged(object? sender, LibVlcTimeChangedEventArgs eventArgs) =>
            SetPosition(TimeSpan.FromMilliseconds(Math.Max(0, eventArgs.Time)));

        private void SetState(PlaybackState state)
        {
            lock (_sync)
            {
                if (_isDisposed || _state == state) return;
                _state = state;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetPosition(TimeSpan position)
        {
            lock (_sync)
            {
                if (_isDisposed || _position == position) return;
                _position = position;
            }
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Unsubscribe()
        {
            _player.Playing -= Player_OnPlaying;
            _player.Paused -= Player_OnPaused;
            _player.Stopped -= Player_OnStopped;
            _player.EndReached -= Player_OnEndReached;
            _player.EncounteredError -= Player_OnEncounteredError;
            _player.TimeChanged -= Player_OnTimeChanged;
        }

        private void ThrowIfDisposed()
        {
            lock (_sync) ObjectDisposedException.ThrowIf(_isDisposed, this);
        }
    }

    internal interface ILibVlcSessionPlayer : IDisposable
    {
        event EventHandler? Playing;

        event EventHandler? Paused;

        event EventHandler? Stopped;

        event EventHandler? EndReached;

        event EventHandler? EncounteredError;

        event EventHandler<LibVlcTimeChangedEventArgs>? TimeChanged;

        bool Play();

        void SetPause(bool pause);

        void Stop();
    }

    internal sealed class LibVlcTimeChangedEventArgs(long time) : EventArgs
    {
        public long Time { get; } = time;
    }

    private sealed class LibVlcSessionPlayer : ILibVlcSessionPlayer
    {
        private readonly MediaPlayer _player;
        private readonly Media _media;

        public LibVlcSessionPlayer(MediaPlayer player, Media media)
        {
            _player = player;
            _media = media;
            _player.Playing += Player_OnPlaying;
            _player.Paused += Player_OnPaused;
            _player.Stopped += Player_OnStopped;
            _player.EndReached += Player_OnEndReached;
            _player.EncounteredError += Player_OnEncounteredError;
            _player.TimeChanged += Player_OnTimeChanged;
        }

        public event EventHandler? Playing;
        public event EventHandler? Paused;
        public event EventHandler? Stopped;
        public event EventHandler? EndReached;
        public event EventHandler? EncounteredError;
        public event EventHandler<LibVlcTimeChangedEventArgs>? TimeChanged;

        public bool Play() => _player.Play(_media);

        public void SetPause(bool pause) => _player.SetPause(pause);

        public void Stop() => _player.Stop();

        public void Dispose()
        {
            _player.Playing -= Player_OnPlaying;
            _player.Paused -= Player_OnPaused;
            _player.Stopped -= Player_OnStopped;
            _player.EndReached -= Player_OnEndReached;
            _player.EncounteredError -= Player_OnEncounteredError;
            _player.TimeChanged -= Player_OnTimeChanged;
        }

        private void Player_OnPlaying(object? sender, EventArgs eventArgs) => Playing?.Invoke(this, eventArgs);
        private void Player_OnPaused(object? sender, EventArgs eventArgs) => Paused?.Invoke(this, eventArgs);
        private void Player_OnStopped(object? sender, EventArgs eventArgs) => Stopped?.Invoke(this, eventArgs);
        private void Player_OnEndReached(object? sender, EventArgs eventArgs) => EndReached?.Invoke(this, eventArgs);
        private void Player_OnEncounteredError(object? sender, EventArgs eventArgs) =>
            EncounteredError?.Invoke(this, eventArgs);
        private void Player_OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs eventArgs) =>
            TimeChanged?.Invoke(this, new LibVlcTimeChangedEventArgs(eventArgs.Time));
    }
}

internal static class LibVlcRuntimeLocator
{
    private const string RuntimePathVariable = "TOLLKAR_LIBVLC_PATH";

    public static IReadOnlyList<LibVlcRuntime> FindAll()
    {
        return FindAll(
            Environment.GetEnvironmentVariable(RuntimePathVariable),
            "/Applications/VLC.app/Contents/MacOS",
            AppContext.BaseDirectory);
    }

    internal static IReadOnlyList<LibVlcRuntime> FindAll(
        string? configuredPath,
        string applicationPath,
        string packagedPath)
    {
        var candidates = new List<string>();
        candidates.Add(packagedPath);
        candidates.Add(applicationPath);
        if (!string.IsNullOrWhiteSpace(configuredPath)) candidates.Add(configuredPath);

        return candidates
            .Select(CreateRuntime)
            .OfType<LibVlcRuntime>()
            .Distinct()
            .ToArray();
    }

    internal static LibVlcRuntime? CreateRuntime(string rootDirectory)
    {
        var libraryDirectory = Directory.Exists(Path.Combine(rootDirectory, "lib"))
            ? Path.Combine(rootDirectory, "lib")
            : rootDirectory;
        var pluginsDirectory = Directory.Exists(Path.Combine(rootDirectory, "plugins"))
            ? Path.Combine(rootDirectory, "plugins")
            : Path.Combine(libraryDirectory, "plugins");
        return File.Exists(Path.Combine(libraryDirectory, "libvlc.dylib")) &&
            File.Exists(Path.Combine(libraryDirectory, "libvlccore.dylib")) &&
            Directory.Exists(pluginsDirectory)
            ? new LibVlcRuntime(libraryDirectory, pluginsDirectory)
            : null;
    }
}

internal sealed record LibVlcRuntime(string LibraryDirectory, string PluginsDirectory);

internal static class LibVlcNativeLibrary
{
    public static (nint VlcHandle, nint CoreHandle) Load(LibVlcRuntime runtime)
    {
        nint coreHandle = 0;
        try
        {
            coreHandle = NativeLibrary.Load(
                Path.Combine(runtime.LibraryDirectory, "libvlccore.dylib"));
            var vlcHandle = NativeLibrary.Load(
                Path.Combine(runtime.LibraryDirectory, "libvlc.dylib"));
            return (vlcHandle, coreHandle);
        }
        catch
        {
            if (coreHandle != 0) NativeLibrary.Free(coreHandle);
            throw;
        }
    }
}
