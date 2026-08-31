using Microsoft.Extensions.Options;
using Tollkar.Application.Library;
using Vostok.Logging.Abstractions;

namespace Tollkar.Web.Catalog;

public sealed class LibrarySyncService(
    ILibraryService library,
    IHostEnvironment environment,
    IOptions<LibrarySyncOptions> options,
    TimeProvider timeProvider,
    ILog log) : BackgroundService
{
    private readonly ILog logger = log.ForContext<LibrarySyncService>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var songsPath = Path.GetFullPath(settings.SongsPath, environment.ContentRootPath);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SynchronizeAsync(songsPath, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.Error(exception, "Song synchronization failed; retrying after {Interval}.",
                        settings.SyncInterval);
                }

                // Wait after completion so long scans never overlap or accumulate missed ticks.
                await Task.Delay(settings.SyncInterval, timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task SynchronizeAsync(string songsPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(songsPath);
        var root = await library.AddRootAsync(songsPath, cancellationToken);
        await foreach (var progress in library.RefreshRootAsync(root.Id, cancellationToken))
        {
            if (!progress.IsCompleted) continue;
            logger.Debug("Song synchronization completed: {Indexed} indexed, {Unchanged} unchanged, {Failed} failed.",
                progress.IndexedSongs, progress.UnchangedFiles, progress.FailedFiles);
            if (progress.FailedFiles > 0)
                logger.Warn("Song synchronization encountered {Failed} file errors.", progress.FailedFiles);
        }
    }
}
