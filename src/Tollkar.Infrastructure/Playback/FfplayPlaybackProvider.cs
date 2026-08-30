using System.Diagnostics;
using Tollkar.Core.Playback;
using Tollkar.Core.Songs;

namespace Tollkar.Infrastructure.Playback;

internal sealed class FfplayPlaybackProvider(string executablePath) : ISongPlaybackProvider
{
    public string FormatProviderId => "video";

    public ValueTask<ISongPlaybackSession> OpenAsync(
        Song song,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(song.Source.FilePath))
        {
            throw new FileNotFoundException("Video file was not found.", song.Source.FilePath);
        }

        return ValueTask.FromResult<ISongPlaybackSession>(
            new FfplayPlaybackSession(song, executablePath));
    }

    public static FfplayPlaybackProvider? TryCreate()
    {
        var executable = FfplayExecutableLocator.Find();
        return executable is null || !FfplayExecutableLocator.IsExecutable(executable)
            ? null
            : new FfplayPlaybackProvider(executable);
    }

    internal static ProcessStartInfo CreateStartInfo(
        string executable,
        Song song,
        TimeSpan startPosition)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        startInfo.ArgumentList.Add("-autoexit");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-nostats");
        startInfo.ArgumentList.Add("-window_title");
        startInfo.ArgumentList.Add($"Tollkar — {song.Metadata.Title}");
        if (startPosition > TimeSpan.Zero)
        {
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(
                startPosition.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(song.Source.FilePath);
        return startInfo;
    }

    private sealed class FfplayPlaybackSession : ISongPlaybackSession
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly string _executablePath;
        private readonly Stopwatch _position = new();
        private readonly Timer _positionTimer;
        private readonly HashSet<Process> _retiredProcesses = [];
        private Process? _process;
        private PlaybackState _state;
        private bool _isStopping;
        private bool _isDisposed;

        public FfplayPlaybackSession(Song song, string executablePath)
        {
            Song = song;
            _executablePath = executablePath;
            _positionTimer = new Timer(
                _ => PositionChanged?.Invoke(this, EventArgs.Empty),
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
        }

        public Song Song { get; }

        public PlaybackState State
        {
            get
            {
                lock (_sync) return _state;
            }
        }

        public TimeSpan Position => _position.Elapsed;

        public event EventHandler? StateChanged;

        public event EventHandler? PositionChanged;

        public async ValueTask PlayAsync(CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                DisposeRetiredProcesses();
                Process? process;
                lock (_sync)
                {
                    ThrowIfDisposed();
                    process = _process;
                }

                if (process is null || process.HasExited)
                {
                    RetireProcess(process);
                    if (State != PlaybackState.Paused) _position.Reset();
                    StartProcess(_position.Elapsed);
                    return;
                }
                if (TrySetState(process, PlaybackState.Playing))
                {
                    _position.Start();
                    _positionTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
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
                DisposeRetiredProcesses();
                var process = GetRunningProcess();
                if (!await TerminateProcessAsync(process)) return;
                _position.Stop();
                _positionTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                SetState(PlaybackState.Paused);
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
                DisposeRetiredProcesses();
                await TerminateProcessAsync();
                _position.Reset();
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
                DisposeRetiredProcesses();
                lock (_sync)
                {
                    if (_isDisposed) return;
                    _isDisposed = true;
                }

                await TerminateProcessAsync();
                DisposeRetiredProcesses();
                await _positionTimer.DisposeAsync();
                _position.Stop();
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private void StartProcess(TimeSpan startPosition)
        {
            var process = Process.Start(CreateStartInfo(_executablePath, Song, startPosition)) ??
                throw new InvalidOperationException("ffplay process could not be started.");
            lock (_sync)
            {
                ThrowIfDisposed();
                _isStopping = false;
                _process = process;
            }
            _position.Start();
            _positionTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
            TrySetState(process, PlaybackState.Playing);
            process.Exited += Process_OnExited;
            process.EnableRaisingEvents = true;
        }

        private Process GetRunningProcess()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_process is null || _process.HasExited)
                {
                    throw new InvalidOperationException("ffplay is not running.");
                }
                return _process;
            }
        }

        private async ValueTask<bool> TerminateProcessAsync(Process? expectedProcess = null)
        {
            Process? process;
            lock (_sync)
            {
                if (expectedProcess is not null && !ReferenceEquals(expectedProcess, _process))
                {
                    return false;
                }
                _isStopping = true;
                process = _process;
                _process = null;
            }

            _positionTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (process is null) return false;
            process.Exited -= Process_OnExited;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
            return true;
        }

        private void Process_OnExited(object? sender, EventArgs eventArgs)
        {
            if (sender is not Process exitedProcess) return;
            PlaybackState? terminalState = null;
            lock (_sync)
            {
                _retiredProcesses.Add(exitedProcess);
                if (!_isDisposed && !_isStopping && ReferenceEquals(sender, _process))
                {
                    _process = null;
                    terminalState = exitedProcess.ExitCode == 0
                        ? PlaybackState.Ended
                        : PlaybackState.Failed;
                    _state = terminalState.Value;
                }
            }
            if (terminalState is null) return;

            _position.Stop();
            _positionTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RetireProcess(Process? process)
        {
            if (process is null) return;
            lock (_sync)
            {
                if (ReferenceEquals(process, _process)) _process = null;
                process.Exited -= Process_OnExited;
                _retiredProcesses.Add(process);
            }
            DisposeRetiredProcesses();
        }

        private void DisposeRetiredProcesses()
        {
            Process[] retired;
            lock (_sync)
            {
                retired = [.. _retiredProcesses];
                _retiredProcesses.Clear();
            }
            foreach (var process in retired) process.Dispose();
        }

        private bool TrySetState(Process process, PlaybackState state)
        {
            lock (_sync)
            {
                if (_isDisposed || !ReferenceEquals(process, _process) || _state == state)
                {
                    return false;
                }
                _state = state;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void SetState(PlaybackState state)
        {
            lock (_sync)
            {
                if (_isDisposed || _state == state) return;
                _state = state;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}

internal static class FfplayExecutableLocator
{
    private static readonly string[] CommonPaths =
    [
        "/opt/homebrew/bin/ffplay",
        "/usr/local/bin/ffplay"
    ];

    public static string? Find() => Find(
        Environment.GetEnvironmentVariable("PATH"),
        File.Exists);

    internal static string? Find(string? path, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        var candidates = (path ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, "ffplay"))
            .Concat(CommonPaths);
        return candidates.FirstOrDefault(fileExists);
    }

    internal static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return false;
        try
        {
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode executableBits =
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (mode & executableBits) != 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
