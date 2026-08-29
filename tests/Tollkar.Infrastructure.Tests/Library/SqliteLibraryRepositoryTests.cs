using Microsoft.Data.Sqlite;
using Tollkar.Application.Library.Models;
using Tollkar.Core.Formats;
using Tollkar.Core.Songs;
using Tollkar.Infrastructure.Library;

namespace Tollkar.Infrastructure.Tests.Library;

public sealed class SqliteLibraryRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"tollkar-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task IndexSurvivesRepositoryRestart()
    {
        var databasePath = Path.Combine(_directory, "library.db");
        var first = new SqliteLibraryRepository(databasePath);
        await first.InitializeAsync();
        var root = await first.AddRootAsync(Path.Combine(_directory, "karaoke"));
        var file = new FileCandidate(
            Path.Combine(_directory, "karaoke", "Кино - Группа крови.mp4"),
            size: 42,
            lastWriteTime: DateTimeOffset.UnixEpoch);
        var metadata = new SongMetadata(
            "Группа крови",
            "Кино",
            TimeSpan.FromMinutes(4),
            SongCapabilities.Audio | SongCapabilities.Video);

        await first.UpsertSongAsync(root.Id, file, "video", 3, metadata);

        var reopened = new SqliteLibraryRepository(databasePath);
        await reopened.InitializeAsync();
        var songs = await reopened.SearchSongsAsync(new LibrarySearchQuery("Группа"));
        var indexedFile = await reopened.GetIndexedFileAsync(file.Path);
        var persistedRoot = await reopened.GetRootAsync(root.Id);
        var readdedRoot = await reopened.AddRootAsync(Path.Combine(_directory, "karaoke"));

        var song = Assert.Single(songs);
        Assert.Equal("Кино", song.Artist);
        Assert.Equal("Группа крови", song.Title);
        Assert.Equal(3, indexedFile?.ProviderVersion);
        Assert.Equal(1, persistedRoot?.SongCount);
        Assert.Equal(1, readdedRoot.SongCount);
    }

    [Fact]
    public async Task AddingSameRootTwiceReturnsExistingRoot()
    {
        var repository = new SqliteLibraryRepository(Path.Combine(_directory, "library.db"));
        await repository.InitializeAsync();
        var path = Path.Combine(_directory, "karaoke");

        var first = await repository.AddRootAsync(path);
        var second = await repository.AddRootAsync(path);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await repository.GetRootsAsync());
    }

    [Fact]
    public async Task SearchIsCaseInsensitiveForCyrillicAndUpsertKeepsSongId()
    {
        var repository = new SqliteLibraryRepository(Path.Combine(_directory, "library.db"));
        await repository.InitializeAsync();
        var root = await repository.AddRootAsync(Path.Combine(_directory, "karaoke"));
        var file = new FileCandidate(Path.Combine(_directory, "song.mp4"), 1, DateTimeOffset.UnixEpoch);
        var firstId = await repository.UpsertSongAsync(
            root.Id, file, "video", 1,
            new SongMetadata("Группа крови", "Кино", null, SongCapabilities.Video));

        var secondId = await repository.UpsertSongAsync(
            root.Id, new FileCandidate(file.Path, 2, file.LastWriteTimeUtc), "video", 2,
            new SongMetadata("Группа крови live", "Кино", null, SongCapabilities.Video));
        var songs = await repository.SearchSongsAsync(new LibrarySearchQuery("кино группа"));

        Assert.Equal(firstId, secondId);
        Assert.Equal("Группа крови live", Assert.Single(songs).Title);
        Assert.Equal(2, (await repository.GetIndexedFileAsync(file.Path))?.ProviderVersion);
    }

    [Fact]
    public async Task InitializeRejectsDatabaseFromNewerApplicationVersion()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "future.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999;";
            await command.ExecuteNonQueryAsync();
        }

        var repository = new SqliteLibraryRepository(databasePath);

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await repository.InitializeAsync());
    }

    [Fact]
    public async Task VersionTwoDatabaseMigratesWithoutLosingSongs()
    {
        var databasePath = Path.Combine(_directory, "version-two.db");
        var repository = new SqliteLibraryRepository(databasePath);
        await repository.InitializeAsync();
        var root = await repository.AddRootAsync(Path.Combine(_directory, "karaoke"));
        await repository.UpsertSongAsync(
            root.Id,
            new FileCandidate(Path.Combine(_directory, "song.mp4"), 1, DateTimeOffset.UnixEpoch),
            "video",
            1,
            new SongMetadata("Song", "Artist", null, SongCapabilities.Video));
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE PlaybackQueue; PRAGMA user_version = 2;";
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteLibraryRepository(databasePath).InitializeAsync();

        Assert.Single(await repository.SearchSongsAsync(new()));
        await using var migrated = new SqliteConnection($"Data Source={databasePath}");
        await migrated.OpenAsync();
        await using var tableCommand = migrated.CreateCommand();
        tableCommand.CommandText = "SELECT COUNT(*) FROM PlaybackQueue;";
        Assert.Equal(0L, await tableCommand.ExecuteScalarAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
