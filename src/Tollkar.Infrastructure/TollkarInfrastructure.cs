using Tollkar.Application.Library;
using Tollkar.Application.Queue;
using Tollkar.Application.Playback;
using Tollkar.Core.Formats;
using Tollkar.Core.Formats.Video;
using Tollkar.Infrastructure.Library;
using Tollkar.Infrastructure.Queue;

namespace Tollkar.Infrastructure;

public static class TollkarInfrastructure
{
    public static ILibraryService CreateLibraryService(string databasePath)
        => CreateServices(databasePath).Library;

    public static TollkarServices CreateServices(string databasePath) => CreateServices(databasePath, "local-desktop");

    public static TollkarServices CreateServices(string databasePath, string userId)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path cannot be empty.", nameof(databasePath));
        }

        var repository = new SqliteLibraryRepository(databasePath);
        var providers = new SongFormatProviderRegistry(
            [new VideoSongFormatProvider()]);
        var scanner = new BackgroundLibraryScanner(repository, providers);
        var library = new LibraryService(repository, scanner);
        var playbackQueue = new PlaybackQueueService(
            new SqlitePlaybackQueueRepository(databasePath),
            userId,
            repository.InitializeAsync);
        return new(library, playbackQueue);
    }

    public static IPlayerService CreatePlayerService(
        ILibraryService library,
        IEnumerable<Tollkar.Core.Playback.ISongPlaybackProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(library);
        return new PlayerService(
            library,
            new Tollkar.Core.Playback.SongPlaybackProviderRegistry(providers));
    }

    public static IQueuePlayerService CreateQueuePlayerService(
        IPlaybackQueueService queue,
        IPlayerService player)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(player);
        return new QueuePlayerService(queue, player);
    }
}
