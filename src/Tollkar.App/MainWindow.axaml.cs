using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Tollkar.Application.Library;

namespace Tollkar.App;

public partial class MainWindow : Window
{
    private readonly ILibraryService _library;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly TaskCompletionSource _initialization = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _activeUiOperation = Task.CompletedTask;
    private bool _isClosed;

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
            await UpdateLibrarySummaryAsync(lifetimeToken);
            _initialization.TrySetResult();
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
            _initialization.TrySetCanceled(lifetimeToken);
        }
        catch (Exception exception)
        {
            _initialization.TrySetException(exception);
            if (!_isClosed)
            {
                Title = "Tollkar — ошибка запуска";
                LibraryStatusText.Text = "Не удалось открыть медиатеку. Перезапустите приложение.";
                SetFolderButtonsEnabled(false);
            }
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _isClosed = true;
        _lifetimeCancellation.Cancel();
        _ = Task.WhenAll(
                Initialization.ContinueWith(_ => { }, TaskScheduler.Default),
                _activeUiOperation)
            .ContinueWith(
            _ => _lifetimeCancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async void AddFolder_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        _activeUiOperation = AddFolderAsync();
        await _activeUiOperation;
    }

    private async Task AddFolderAsync()
    {
        SetFolderButtonsEnabled(false);
        try
        {
            await Initialization;
            var folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Выберите папку с караоке-файлами",
                    AllowMultiple = false
                });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (path is null) return;

            var root = await _library.AddRootAsync(
                path,
                _lifetimeCancellation.Token);
            await foreach (var progress in _library
                .RefreshRootAsync(root.Id, _lifetimeCancellation.Token))
            {
                if (_isClosed) return;
                LibraryStatusText.Text = progress.IsCompleted
                    ? $"Индексация завершена · найдено {progress.IndexedSongs + progress.UnchangedFiles}"
                    : $"Индексирование · файлов {progress.DiscoveredFiles}, добавлено {progress.IndexedSongs}";
            }
            await UpdateLibrarySummaryAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_isClosed)
            {
                LibraryStatusText.Text = $"Не удалось обновить медиатеку: {exception.Message}";
            }
        }
        finally
        {
            if (!_isClosed) SetFolderButtonsEnabled(true);
        }
    }

    private void SetFolderButtonsEnabled(bool isEnabled)
    {
        AddFolderButton.IsEnabled = isEnabled;
        EmptyAddFolderButton.IsEnabled = isEnabled;
    }

    private async Task UpdateLibrarySummaryAsync(CancellationToken cancellationToken)
    {
        var roots = await _library.GetRootsAsync(cancellationToken);
        if (_isClosed) return;
        var songCount = roots.Sum(root => root.SongCount);
        LibraryStatusText.Text = songCount == 0
            ? "В медиатеке пока нет песен"
            : $"В медиатеке {songCount} песен";
    }
}
