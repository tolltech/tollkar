using Avalonia.Controls;
using Tollkar.Application.Library;

namespace Tollkar.App;

public partial class MainWindow : Window
{
    private readonly ILibraryService _library;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly TaskCompletionSource _initialization = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public MainWindow(ILibraryService library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    internal Task Initialization => _initialization.Task;

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        var lifetimeToken = _lifetimeCancellation.Token;
        try
        {
            await _library.InitializeAsync(lifetimeToken);
            _initialization.TrySetResult();
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
            _initialization.TrySetCanceled(lifetimeToken);
        }
        catch (Exception exception)
        {
            _initialization.TrySetException(exception);
            Title = "Tollkar — ошибка запуска";
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _lifetimeCancellation.Cancel();
        _ = Initialization.ContinueWith(
            _ => _lifetimeCancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
