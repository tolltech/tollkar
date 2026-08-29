using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Tollkar.Application.Library;
using Tollkar.Application.Library.Models;
using Tollkar.Application.Queue;
using Tollkar.Application.Queue.Models;
using Tollkar.Application.Playback;
using Tollkar.Application.Playback.Models;
using Tollkar.Core.Songs;
using Tollkar.Infrastructure;
#if MACOS
using Tollkar.App.Platforms.MacOS;
#endif

namespace Tollkar.App;

public partial class MainWindow : Window
{
    private readonly ILibraryService _library;
    private readonly IPlaybackQueueService _playbackQueue;
    private readonly IQueuePlayerService? _queuePlayer = null;
#if MACOS
    private readonly MacVideoHost? _videoHost;
#endif
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly TaskCompletionSource _initialization = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Task> _uiOperations = [];
    private Task _activeUiOperation = Task.CompletedTask;
    private long _searchVersion;
    private bool _hasVisibleSongs;
    private bool _isClosed;

    public MainWindow(ILibraryService library, IPlaybackQueueService playbackQueue)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _playbackQueue = playbackQueue ?? throw new ArgumentNullException(nameof(playbackQueue));
        InitializeComponent();
#if MACOS
        _videoHost = new MacVideoHost();
        VideoSurfaceHost.Content = _videoHost;
        var player = TollkarInfrastructure.CreatePlayerService(
            _library,
            [new AvFoundationPlaybackProvider(_videoHost)]);
        _queuePlayer = TollkarInfrastructure.CreateQueuePlayerService(_playbackQueue, player);
        _queuePlayer.SnapshotChanged += Player_OnSnapshotChanged;
        _queuePlayer.QueueChanged += QueuePlayer_OnQueueChanged;
        _queuePlayer.PlaybackFailed += QueuePlayer_OnPlaybackFailed;
#endif
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
            await _playbackQueue.InitializeAsync(lifetimeToken);
            await UpdateLibrarySummaryAsync(lifetimeToken);
            await ReloadSongsAsync(SearchBox.Text, lifetimeToken);
            await ReloadQueueAsync(lifetimeToken);
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
        if (_queuePlayer is not null)
        {
            _queuePlayer.SnapshotChanged -= Player_OnSnapshotChanged;
            _queuePlayer.QueueChanged -= QueuePlayer_OnQueueChanged;
            _queuePlayer.PlaybackFailed -= QueuePlayer_OnPlaybackFailed;
        }
        SearchBox.TextChanged -= SearchBox_OnTextChanged;
        _ = Task.WhenAll(
                Initialization.ContinueWith(_ => { }, TaskScheduler.Default),
                _activeUiOperation,
                Task.WhenAll(_uiOperations.ToArray()),
                DisposePlaybackAsync())
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

    private void AddSongToQueue_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        TrackUiOperation(ChangeQueueAsync(sender, QueueChange.Add));

    private void RemoveQueueItem_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        TrackUiOperation(ChangeQueueAsync(sender, QueueChange.Remove));

    private void MoveQueueItemUp_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        TrackUiOperation(ChangeQueueAsync(sender, QueueChange.MoveUp));

    private void MoveQueueItemDown_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        TrackUiOperation(ChangeQueueAsync(sender, QueueChange.MoveDown));

    private void PlayQueueItem_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        TrackUiOperation(PlayQueueItemAsync(sender));

    private void PlayPause_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        TrackUiOperation(TogglePlaybackAsync());

    private void Next_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        TrackUiOperation(AdvanceQueueAsync());

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
        _hasVisibleSongs = songs.Count > 0;
        UpdateLibraryContentVisibility();

        var isSearching = !string.IsNullOrWhiteSpace(searchText);
        EmptyLibraryTitle.Text = isSearching ? "Ничего не найдено" : "Добавьте музыку";
        EmptyLibraryDescription.Text = isSearching
            ? "Попробуйте изменить название песни или исполнителя."
            : "Выберите папку с караоке-файлами — песни будут появляться здесь по мере индексации.";
        EmptyAddFolderButton.IsVisible = !isSearching;
    }

    private async Task ChangeQueueAsync(object? sender, QueueChange change)
    {
        if (sender is not Button { CommandParameter: Guid id }) return;

        try
        {
            await Initialization;
            switch (change)
            {
                case QueueChange.Add:
                    await _playbackQueue.AddAsync(id, _lifetimeCancellation.Token);
                    break;
                case QueueChange.Remove:
                    await _playbackQueue.RemoveAsync(id, _lifetimeCancellation.Token);
                    break;
                case QueueChange.MoveUp:
                case QueueChange.MoveDown:
                    var offset = change == QueueChange.MoveUp ? -1 : 1;
                    await _playbackQueue.MoveByAsync(id, offset, _lifetimeCancellation.Token);
                    break;
            }

            await ReloadQueueAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_isClosed)
            {
                LibraryStatusText.Text = $"Не удалось изменить очередь: {exception.Message}";
            }
        }
    }

    private async Task ReloadQueueAsync(CancellationToken cancellationToken)
    {
        var items = await _playbackQueue.GetItemsAsync(cancellationToken);
        if (_isClosed) return;

        QueueList.ItemsSource = items
            .Select(item => QueueListItem.FromQueueItem(item, _queuePlayer is not null))
            .ToArray();
        QueueList.IsVisible = items.Count > 0;
        EmptyQueuePanel.IsVisible = items.Count == 0;
        QueueCountText.Text = items.Count.ToString();
        NextButton.IsEnabled = _queuePlayer is not null && items.Count > 0;
    }

    private async Task PlayQueueItemAsync(object? sender)
    {
        if (_queuePlayer is null || sender is not Button { CommandParameter: Guid queueItemId }) return;
        try
        {
            await Initialization;
            await _queuePlayer.PlayQueueItemAsync(queueItemId, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowPlaybackError(exception);
        }
    }

    private async Task TogglePlaybackAsync()
    {
        if (_queuePlayer is null) return;
        try
        {
            await _queuePlayer.TogglePauseAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowPlaybackError(exception);
        }
    }

    private async Task AdvanceQueueAsync()
    {
        if (_queuePlayer is null) return;
        try
        {
            await _queuePlayer.NextAsync(_lifetimeCancellation.Token);
            await ReloadQueueAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowPlaybackError(exception);
        }
    }

    private void Player_OnSnapshotChanged(object? sender, EventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isClosed || _queuePlayer is null) return;
            UpdatePlayerUi(_queuePlayer.Snapshot);
        });
    }

    private void QueuePlayer_OnQueueChanged(object? sender, EventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isClosed) TrackUiOperation(ReloadQueueAsync(_lifetimeCancellation.Token));
        });
    }

    private void QueuePlayer_OnPlaybackFailed(object? sender, QueuePlaybackFailedEventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() => ShowPlaybackError(eventArgs.Exception));

    private async Task DisposePlaybackAsync()
    {
        if (_queuePlayer is not null)
        {
            await _queuePlayer.DisposeAsync();
        }
#if MACOS
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            VideoSurfaceHost.Content = null;
            _videoHost?.Dispose();
        });
#endif
    }

    private void UpdatePlayerUi(PlayerSnapshot snapshot)
    {
        var hasSong = snapshot.SongId is not null;
        NowPlayingTitleText.Text = snapshot.Title ?? "Ничего не играет";
        NowPlayingArtistText.Text = snapshot.Artist ?? "Выберите песню в медиатеке";
        PlayPauseButton.IsEnabled = hasSong && snapshot.State is not
            (Tollkar.Core.Playback.PlaybackState.Ended or Tollkar.Core.Playback.PlaybackState.Failed);
        Avalonia.Automation.AutomationProperties.SetName(
            PlayPauseButton,
            snapshot.State == Tollkar.Core.Playback.PlaybackState.Playing
                ? "Пауза"
                : "Воспроизвести");
        ToolTip.SetTip(
            PlayPauseButton,
            snapshot.State == Tollkar.Core.Playback.PlaybackState.Playing
                ? "Пауза"
                : "Воспроизвести");
        PlayPauseIcon.Data = Geometry.Parse(snapshot.State == Tollkar.Core.Playback.PlaybackState.Playing
            ? "M5,3 L8,3 L8,15 L5,15 Z M11,3 L14,3 L14,15 L11,15 Z"
            : "M5,3 L16,9 L5,15 Z");
        PlayerVideoPanel.IsVisible = hasSong;
        UpdateLibraryContentVisibility();
    }

    private void UpdateLibraryContentVisibility()
    {
        var showLibrary = !PlayerVideoPanel.IsVisible;
        SongsList.IsVisible = showLibrary && _hasVisibleSongs;
        EmptyLibraryPanel.IsVisible = showLibrary && !_hasVisibleSongs;
    }

    private void ShowPlaybackError(Exception exception)
    {
        if (_isClosed) return;
        LibraryStatusText.Text = $"Не удалось воспроизвести видео: {exception.Message}";
        PlayerVideoPanel.IsVisible = false;
        UpdateLibraryContentVisibility();
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
        Guid Id,
        string Title,
        string Artist,
        string Format,
        string Duration)
    {
        public static SongListItem FromSong(LibrarySong song) => new(
            song.Id,
            song.Title,
            string.IsNullOrWhiteSpace(song.Artist) ? "Неизвестный исполнитель" : song.Artist,
            song.Capabilities.HasFlag(SongCapabilities.Video) ? "Видео" : "Караоке",
            song.Duration is { } duration ? $"{(int)duration.TotalMinutes}:{duration.Seconds:00}" : string.Empty);
    }

    private sealed record QueueListItem(Guid Id, string Title, string Artist, bool CanPlay)
    {
        public static QueueListItem FromQueueItem(PlaybackQueueItem item, bool canPlay) => new(
            item.Id,
            item.Title,
            string.IsNullOrWhiteSpace(item.Artist) ? "Неизвестный исполнитель" : item.Artist,
            canPlay);
    }

    private enum QueueChange
    {
        Add,
        Remove,
        MoveUp,
        MoveDown
    }
}
