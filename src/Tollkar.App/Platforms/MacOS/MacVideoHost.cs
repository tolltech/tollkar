using AVFoundation;
using AVKit;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Tollkar.App.Platforms.MacOS;

internal sealed class MacVideoHost : NativeControlHost, IDisposable
{
    private AVPlayerView? _playerView;
    private bool _isDisposed;

    public AVPlayer Player { get; } = new();

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _playerView = new AVPlayerView
        {
            Player = Player,
            ControlsStyle = AVPlayerViewControlsStyle.None
        };
        return new PlatformHandle(_playerView.Handle, "NSView");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (!_isDisposed)
        {
            Player.Pause();
            Player.ReplaceCurrentItemWithPlayerItem(null);
        }
        _playerView?.Dispose();
        _playerView = null;
        base.DestroyNativeControlCore(control);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Player.Pause();
        Player.ReplaceCurrentItemWithPlayerItem(null);
        Player.Dispose();
    }
}
