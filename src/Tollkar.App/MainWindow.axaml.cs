using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Tollkar.Application.Library;
using Tollkar.Application.Library.Models;
using Tollkar.Core.Songs;

namespace Tollkar.App;

public partial class MainWindow : Window
{
    private readonly ILibraryService _library;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly TaskCompletionSource _initialization = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Task> _uiOperations = [];
    private Task _activeUiOperation = Task.CompletedTask;
    private long _searchVersion;
    private bool _isClosed;

    public MainWindow(ILibraryService library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        InitializeComponent();
        SearchBox.TextChanged += SearchBox_OnTextChanged;
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
            await ReloadSongsAsync(SearchBox.Text, lifetimeToken);
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
        SearchBox.TextChanged -= SearchBox_OnTextChanged;
        _ = Task.WhenAll(
                Initialization.ContinueWith(_ => { }, TaskScheduler.Default),
                _activeUiOperation,
                Task.WhenAll(_uiOperations.ToArray()))
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

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        var operation = SearchAfterDelayAsync(++_searchVersion, SearchBox.Text);
        TrackUiOperation(operation);
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
                var searchVersion = _searchVersion;
                LibraryStatusText.Text = progress.IsCompleted
                    ? $"Индексация завершена · найдено {progress.IndexedSongs + progress.UnchangedFiles}"
                    : $"Индексирование · файлов {progress.DiscoveredFiles}, добавлено {progress.IndexedSongs}";
                await ReloadSongsAsync(SearchBox.Text, _lifetimeCancellation.Token, searchVersion);
            }
            await UpdateLibrarySummaryAsync(_lifetimeCancellation.Token);
            await ReloadSongsAsync(SearchBox.Text, _lifetimeCancellation.Token, _searchVersion);
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

    private async Task SearchAfterDelayAsync(long version, string? searchText)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), _lifetimeCancellation.Token);
            await Initialization;
            if (version != _searchVersion) return;
            await ReloadSongsAsync(searchText, _lifetimeCancellation.Token, version);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_isClosed && version == _searchVersion)
            {
                LibraryStatusText.Text = $"Не удалось выполнить поиск: {exception.Message}";
            }
        }
    }

    private async Task ReloadSongsAsync(
        string? searchText,
        CancellationToken cancellationToken,
        long? expectedSearchVersion = null)
    {
        var songs = await _library.SearchSongsAsync(
            new LibrarySearchQuery(searchText, LibrarySearchQuery.MaximumLimit),
            cancellationToken);
        if (_isClosed || expectedSearchVersion is not null && expectedSearchVersion != _searchVersion) return;

        SongsList.ItemsSource = songs.Select(SongListItem.FromSong).ToArray();
        SongsList.IsVisible = songs.Count > 0;
        EmptyLibraryPanel.IsVisible = songs.Count == 0;

        var isSearching = !string.IsNullOrWhiteSpace(searchText);
        EmptyLibraryTitle.Text = isSearching ? "Ничего не найдено" : "Добавьте музыку";
        EmptyLibraryDescription.Text = isSearching
            ? "Попробуйте изменить название песни или исполнителя."
            : "Выберите папку с караоке-файлами — песни будут появляться здесь по мере индексации.";
        EmptyAddFolderButton.IsVisible = !isSearching;
    }

    private void TrackUiOperation(Task operation)
    {
        _uiOperations.Add(operation);
        _ = operation.ContinueWith(
            _ => _uiOperations.Remove(operation),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.FromCurrentSynchronizationContext());
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

    private sealed record SongListItem(
        string Title,
        string Artist,
        string Format,
        string Duration)
    {
        public static SongListItem FromSong(LibrarySong song) => new(
            song.Title,
            string.IsNullOrWhiteSpace(song.Artist) ? "Неизвестный исполнитель" : song.Artist,
            song.Capabilities.HasFlag(SongCapabilities.Video) ? "Видео" : "Караоке",
            song.Duration is { } duration ? $"{(int)duration.TotalMinutes}:{duration.Seconds:00}" : string.Empty);
    }
}
