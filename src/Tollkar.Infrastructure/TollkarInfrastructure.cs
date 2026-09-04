using Tollkar.Application.Library;
using Tollkar.Application.Queue;
using Tollkar.Application.Playback;
using Tollkar.Core.Formats;
using Tollkar.Core.Formats.Kfn;
using Tollkar.Core.Formats.Video;
using Tollkar.Infrastructure.Library;
using Tollkar.Infrastructure.Queue;

namespace Tollkar.Infrastructure;

public static class TollkarInfrastructure
{
    public static ILibraryService CreateLibraryService(string databasePath)
        => CreateLibraryServiceCore(databasePath);

    public static TollkarServices CreateServices(string databasePath, string userId)
    {
        var library = CreateLibraryServiceCore(databasePath);
        var playbackQueue = new PlaybackQueueService(
            new SqlitePlaybackQueueRepository(databasePath),
            userId,
            library.InitializeAsync);
        return new(library, playbackQueue);
    }

    private static ILibraryService CreateLibraryServiceCore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path cannot be empty.", nameof(databasePath));
        }

        var repository = new SqliteLibraryRepository(databasePath);
        var providers = new SongFormatProviderRegistry(
            [new VideoSongFormatProvider(), new KfnSongFormatProvider()]);
        var scanner = new BackgroundLibraryScanner(repository, providers);
        return new LibraryService(repository, scanner);
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
