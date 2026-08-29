using Tollkar.Application.Library;
using Tollkar.Core.Formats;
using Tollkar.Core.Formats.Video;
using Tollkar.Infrastructure.Library;

namespace Tollkar.Infrastructure;

public static class TollkarInfrastructure
{
    public static ILibraryService CreateLibraryService(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path cannot be empty.", nameof(databasePath));
        }

        var repository = new SqliteLibraryRepository(databasePath);
        var providers = new SongFormatProviderRegistry(
            [new VideoSongFormatProvider()]);
        var scanner = new BackgroundLibraryScanner(repository, providers);
        return new LibraryService(repository, scanner);
    }
}
