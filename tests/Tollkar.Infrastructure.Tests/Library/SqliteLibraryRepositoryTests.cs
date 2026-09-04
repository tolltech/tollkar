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
        var song = Assert.Single(songs);
        var indexedFile = await reopened.GetIndexedFileAsync(file.Path);
        var playableSong = await reopened.GetSongAsync(song.Id);
        var persistedRoot = await reopened.GetRootAsync(root.Id);
        var readdedRoot = await reopened.AddRootAsync(Path.Combine(_directory, "karaoke"));

        Assert.Equal("Кино", song.Artist);
        Assert.Equal("Группа крови", song.Title);
        Assert.Equal(0, song.PlayCount);
        Assert.Equal(3, indexedFile?.ProviderVersion);
        Assert.Equal(Path.GetFullPath(file.Path), playableSong?.Source.FilePath);
        Assert.Equal("video", playableSong?.Source.ProviderId);
        Assert.Equal(1, persistedRoot?.SongCount);
        Assert.Equal(1, readdedRoot.SongCount);
    }

    [Fact]
    public async Task PlaybackCountStartsAtZeroAndSurvivesRestart()
    {
        var databasePath = Path.Combine(_directory, "library.db");
        var repository = new SqliteLibraryRepository(databasePath);
        await repository.InitializeAsync();
        var root = await repository.AddRootAsync(Path.Combine(_directory, "karaoke"));
        var songId = await IndexSongAsync(repository, root.Id, Path.Combine(_directory, "song.mp4"), "Song");

        await repository.IncrementPlayCountAsync(songId);
        await repository.IncrementPlayCountAsync(songId);

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        Assert.Equal(6L, await versionCommand.ExecuteScalarAsync());
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PlayCount FROM Songs WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", songId.ToString());
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task SearchLabelsSongsWithTheirFirstFolderUnderTheRoot()
    {
        var repository = new SqliteLibraryRepository(Path.Combine(_directory, "library.db"));
        await repository.InitializeAsync();
        var rootPath = Path.Combine(_directory, "songs");
        var root = await repository.AddRootAsync(rootPath);
        await IndexSongAsync(repository, root.Id, Path.Combine(rootPath, "Кино - Группа крови.mp4"), "Группа крови");
        await IndexSongAsync(repository, root.Id, Path.Combine(rootPath, "Сборник", "Кино - Звезда.mp4"), "Звезда");
        await IndexSongAsync(repository, root.Id, Path.Combine(rootPath, "Сборник", "Диск 2", "Кино - Кукушка.mp4"), "Кукушка");
        var otherRoot = await repository.AddRootAsync(Path.Combine(_directory, "karaoke"));
        await IndexSongAsync(repository, otherRoot.Id, Path.Combine(_directory, "karaoke", "Лучшее", "Кино - Легенда.mp4"), "Легенда");

        var all = (await repository.SearchSongsAsync(new()))
            .ToDictionary(song => song.Title, song => song.Folder);
        var matched = await repository.SearchSongsAsync(new LibrarySearchQuery("Звезда"));

        Assert.Null(all["Группа крови"]);
        Assert.Equal("Сборник", all["Звезда"]);
        Assert.Equal("Сборник", all["Кукушка"]);
        Assert.Equal("Лучшее", all["Легенда"]);
        Assert.Equal("Сборник", Assert.Single(matched).Folder);
    }

    [Fact]
    public async Task SearchOrdersSongsByPlayCountThenFolderArtistAndTitle()
    {
        var repository = new SqliteLibraryRepository(Path.Combine(_directory, "library.db"));
        await repository.InitializeAsync();
        var rootPath = Path.Combine(_directory, "songs");
        var root = await repository.AddRootAsync(rootPath);
        await IndexSongAsync(repository, root.Id, Path.Combine(rootPath, "Zeta - Root.mp4"), "Root", "Zeta");
        await IndexSongAsync(repository, root.Id, Path.Combine(rootPath, "Bravo", "Zeta - B.mp4"), "B", "Zeta");
        await IndexSongAsync(repository, root.Id, Path.Combine(rootPath, "Alpha", "Zeta - A.mp4"), "A", "Zeta");
        await IndexSongAsync(repository, root.Id, Path.Combine(rootPath, "Alpha", "Alpha - A.mp4"), "A", "Alpha");
        await IndexSongAsync(repository, root.Id, Path.Combine(rootPath, "Bravo", "Alpha - A.mp4"), "A", "Alpha");
        var popularSong = Assert.Single(await repository.SearchSongsAsync(new LibrarySearchQuery("Root")));
        await repository.IncrementPlayCountAsync(popularSong.Id);

        var songs = await repository.SearchSongsAsync(new());

        Assert.Equal(
            new[] { "Root", "A", "A", "A", "B" },
            songs.Select(song => song.Title));
        Assert.Equal(
            new string?[] { null, "Alpha", "Alpha", "Bravo", "Bravo" },
            songs.Select(song => song.Folder));
        Assert.Equal(
            new string?[] { "Zeta", "Alpha", "Zeta", "Alpha", "Zeta" },
            songs.Select(song => song.Artist));

        var limited = await repository.SearchSongsAsync(new LibrarySearchQuery(Limit: 2));
        Assert.Equal(new[] { "Root", "A" }, limited.Select(song => song.Title));
        Assert.Equal(new string?[] { null, "Alpha" }, limited.Select(song => song.Folder));

        await repository.IncrementPlayCountAsync((await repository.SearchSongsAsync(new LibrarySearchQuery("B"))).Single().Id);
        var reordered = await repository.SearchSongsAsync(new());
        Assert.Equal(new[] { "Root", "B", "A", "A", "A" }, reordered.Select(song => song.Title));
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
            command.CommandText = "DROP TABLE PlaybackQueue; ALTER TABLE Songs DROP COLUMN PlayCount; PRAGMA user_version = 2;";
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

    [Fact]
    public async Task VersionThreeQueueIsPreservedAsAnIsolatedLegacyQueue()
    {
        var databasePath = Path.Combine(_directory, "version-three.db");
        var services = TollkarInfrastructure.CreateServices(databasePath, "legacy-owner");
        await services.Library.InitializeAsync();
        var repository = new SqliteLibraryRepository(databasePath);
        var root = await repository.AddRootAsync(Path.Combine(_directory, "karaoke"));
        var songId = await repository.UpsertSongAsync(root.Id,
            new FileCandidate(Path.Combine(_directory, "song.mp4"), 1, DateTimeOffset.UnixEpoch),
            "video", 1, new SongMetadata("Song", "Artist", null, SongCapabilities.Video));
        await services.PlaybackQueue.AddAsync(songId);
        var original = Assert.Single(await services.PlaybackQueue.GetItemsAsync());
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
            DROP INDEX IX_PlaybackQueue_UserId_Position;
            ALTER TABLE PlaybackQueue DROP COLUMN UserId;
            ALTER TABLE Songs DROP COLUMN PlayCount;
            CREATE INDEX IX_PlaybackQueue_Position ON PlaybackQueue(Position);
            PRAGMA user_version = 3;
            """;
            await command.ExecuteNonQueryAsync();
        }

        // Simulate a process restart after restoring the old schema, without cached schema metadata.
        using (var pooled = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = Path.GetFullPath(databasePath), ForeignKeys = true }.ToString()))
            SqliteConnection.ClearPool(pooled);
        var reopened = TollkarInfrastructure.CreateServices(databasePath, "legacy-owner");
        await reopened.Library.InitializeAsync();
        await reopened.Library.InitializeAsync();
        Assert.Empty(await reopened.PlaybackQueue.GetItemsAsync());
        Assert.Empty(await TollkarInfrastructure.CreateServices(databasePath, "alice").PlaybackQueue.GetItemsAsync());
        Assert.Empty(await TollkarInfrastructure.CreateServices(databasePath, "bob").PlaybackQueue.GetItemsAsync());
        await using var migrated = new SqliteConnection($"Data Source={databasePath}");
        await migrated.OpenAsync();
        await using var legacyQueueCommand = migrated.CreateCommand();
        legacyQueueCommand.CommandText = "SELECT Id,UserId FROM PlaybackQueue WHERE UserId='__legacy_queue__';";
        await using var reader = await legacyQueueCommand.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(original.Id.ToString(), reader.GetString(0));
        Assert.Equal("__legacy_queue__", reader.GetString(1));
    }

    [Fact]
    public async Task VersionFiveLegacyQueueIsPreservedAsAnIsolatedLegacyQueue()
    {
        var databasePath = Path.Combine(_directory, "version-five.db");
        var services = TollkarInfrastructure.CreateServices(databasePath, "alice");
        await services.Library.InitializeAsync();
        var repository = new SqliteLibraryRepository(databasePath);
        var root = await repository.AddRootAsync(Path.Combine(_directory, "karaoke"));
        var songId = await repository.UpsertSongAsync(root.Id,
            new FileCandidate(Path.Combine(_directory, "song.mp4"), 1, DateTimeOffset.UnixEpoch),
            "video", 1, new SongMetadata("Song", "Artist", null, SongCapabilities.Video));
        await services.PlaybackQueue.AddAsync(songId);
        var legacyItem = Assert.Single(await services.PlaybackQueue.GetItemsAsync());
        var bobServices = TollkarInfrastructure.CreateServices(databasePath, "bob");
        await bobServices.PlaybackQueue.AddAsync(songId);
        var bobItem = Assert.Single(await bobServices.PlaybackQueue.GetItemsAsync());
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE PlaybackQueue SET UserId='local-desktop' WHERE Id=$id; PRAGMA user_version = 5;";
            command.Parameters.AddWithValue("$id", legacyItem.Id.ToString());
            await command.ExecuteNonQueryAsync();
        }

        using (var pooled = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = Path.GetFullPath(databasePath), ForeignKeys = true }.ToString()))
            SqliteConnection.ClearPool(pooled);
        await new SqliteLibraryRepository(databasePath).InitializeAsync();

        Assert.Empty(await TollkarInfrastructure.CreateServices(databasePath, "alice").PlaybackQueue.GetItemsAsync());
        Assert.Equal([bobItem], await TollkarInfrastructure.CreateServices(databasePath, "bob").PlaybackQueue.GetItemsAsync());
        await using var migrated = new SqliteConnection($"Data Source={databasePath}");
        await migrated.OpenAsync();
        await using var legacyQueueCommand = migrated.CreateCommand();
        legacyQueueCommand.CommandText = "SELECT Id,UserId FROM PlaybackQueue WHERE UserId='__legacy_queue__';";
        await using var reader = await legacyQueueCommand.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(legacyItem.Id.ToString(), reader.GetString(0));
        Assert.Equal("__legacy_queue__", reader.GetString(1));
    }

    private static ValueTask<Guid> IndexSongAsync(
        SqliteLibraryRepository repository,
        Guid rootId,
        string path,
        string title,
        string artist = "Кино") =>
        repository.UpsertSongAsync(
            rootId,
            new FileCandidate(path, size: 1, lastWriteTime: DateTimeOffset.UnixEpoch),
            "video",
            1,
            new SongMetadata(title, artist, null, SongCapabilities.Video));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
