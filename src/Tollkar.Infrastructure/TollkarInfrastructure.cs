using Tollkar.Application.Library;
using Tollkar.Application.Queue;
using Tollkar.Core.Formats;
using Tollkar.Core.Formats.Video;
using Tollkar.Infrastructure.Library;
using Tollkar.Infrastructure.Queue;

namespace Tollkar.Infrastructure;

public static class TollkarInfrastructure
{
    public static ILibraryService CreateLibraryService(string databasePath)
        => CreateServices(databasePath).Library;

    public static TollkarServices CreateServices(string databasePath)
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
            repository.InitializeAsync);
        return new(library, playbackQueue);
    }
}
