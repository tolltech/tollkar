using Microsoft.Data.Sqlite;
using Tollkar.Application.Library.Models;
using Tollkar.Application.Library.Persistence;
using Tollkar.Core.Songs;

namespace Tollkar.Infrastructure.Library;

internal sealed class SqliteLibraryRepository(string databasePath) : ILibraryRepository
{
    private const int SchemaVersion = 2;

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath),
        ForeignKeys = true
    }.ToString();

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(new SqliteConnectionStringBuilder(_connectionString).DataSource);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
        var version = Convert.ToInt32(await ExecuteScalarAsync(
            connection, "PRAGMA user_version;", cancellationToken));
        if (version > SchemaVersion)
        {
            throw new NotSupportedException(
                $"Database version {version} is newer than supported version {SchemaVersion}.");
        }

        if (version == 0)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
            CREATE TABLE IF NOT EXISTS LibraryRoots (
                Id TEXT PRIMARY KEY, Path TEXT NOT NULL UNIQUE, DisplayName TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Songs (
                Id TEXT PRIMARY KEY, RootId TEXT NOT NULL, Title TEXT NOT NULL, Artist TEXT NULL,
                DurationTicks INTEGER NULL, Capabilities INTEGER NOT NULL,
                FOREIGN KEY (RootId) REFERENCES LibraryRoots(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS Files (
                Path TEXT PRIMARY KEY, SongId TEXT NOT NULL UNIQUE, Size INTEGER NOT NULL,
                LastWriteTimeUtc TEXT NOT NULL, ProviderId TEXT NOT NULL, ProviderVersion INTEGER NOT NULL,
                FOREIGN KEY (SongId) REFERENCES Songs(Id) ON DELETE CASCADE);
            CREATE VIRTUAL TABLE IF NOT EXISTS SongSearch USING fts5(
                SongId UNINDEXED, Title, Artist,
                tokenize = 'unicode61 remove_diacritics 2');
            PRAGMA user_version = 1;
            """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            version = 1;
        }

        if (version == 1)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "ALTER TABLE Files ADD COLUMN LastSeenScanId TEXT NULL; PRAGMA user_version = 2;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async ValueTask<LibraryRootRecord> AddRootAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var displayName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
        if (displayName.Length == 0) displayName = fullPath;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO LibraryRoots(Id, Path, DisplayName) VALUES($id,$path,$name) ON CONFLICT(Path) DO NOTHING; SELECT r.Id,r.DisplayName,COUNT(s.Id) FROM LibraryRoots r LEFT JOIN Songs s ON s.RootId=r.Id WHERE r.Path=$path GROUP BY r.Id;";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$path", fullPath);
        command.Parameters.AddWithValue("$name", displayName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(Guid.Parse(reader.GetString(0)), fullPath, reader.GetString(1), reader.GetInt32(2));
    }

    public async ValueTask<LibraryRootRecord?> GetRootAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT r.Path,r.DisplayName,COUNT(s.Id) FROM LibraryRoots r LEFT JOIN Songs s ON s.RootId=r.Id WHERE r.Id=$id GROUP BY r.Id;";
        command.Parameters.AddWithValue("$id", rootId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(rootId, reader.GetString(0), reader.GetString(1), reader.GetInt32(2)) : null;
    }

    public async ValueTask<IReadOnlyList<LibraryRootRecord>> GetRootsAsync(CancellationToken cancellationToken = default)
    {
        var roots = new List<LibraryRootRecord>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT r.Id,r.Path,r.DisplayName,COUNT(s.Id) FROM LibraryRoots r LEFT JOIN Songs s ON s.RootId=r.Id GROUP BY r.Id ORDER BY r.DisplayName;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) roots.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        return roots;
    }

    public async ValueTask<IReadOnlyList<LibrarySong>> SearchSongsAsync(LibrarySearchQuery query, CancellationToken cancellationToken = default)
    {
        var songs = new List<LibrarySong>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var text = query.Text?.Trim() ?? "";
        command.CommandText = text.Length == 0
            ? "SELECT Id,Title,Artist,DurationTicks,Capabilities FROM Songs ORDER BY Artist,Title LIMIT $limit;"
            : "SELECT s.Id,s.Title,s.Artist,s.DurationTicks,s.Capabilities FROM SongSearch f JOIN Songs s ON s.Id=f.SongId WHERE SongSearch MATCH $match ORDER BY s.Artist,s.Title LIMIT $limit;";
        if (text.Length > 0)
        {
            command.Parameters.AddWithValue("$match", ToFtsQuery(text));
        }
        command.Parameters.AddWithValue("$limit", query.ValidatedLimit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) songs.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : TimeSpan.FromTicks(reader.GetInt64(3)), (SongCapabilities)reader.GetInt32(4)));
        return songs;
    }

    public async ValueTask<IndexedFileRecord?> GetIndexedFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Size,LastWriteTimeUtc,ProviderId,ProviderVersion FROM Files WHERE Path=$path;";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                Path.GetFullPath(path),
                reader.GetInt64(0),
                DateTimeOffset.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetString(2),
                reader.GetInt32(3))
            : null;
    }

    public async ValueTask<Guid> UpsertSongAsync(
        Guid rootId,
        Tollkar.Core.Formats.FileCandidate file,
        string providerId,
        int providerVersion,
        SongMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var path = Path.GetFullPath(file.Path);
        var songId = await FindSongIdAsync(connection, transaction, path, cancellationToken) ?? Guid.NewGuid();

        await using (var song = connection.CreateCommand())
        {
            song.Transaction = (SqliteTransaction)transaction;
            song.CommandText = "INSERT INTO Songs(Id,RootId,Title,Artist,DurationTicks,Capabilities) VALUES($id,$root,$title,$artist,$duration,$capabilities) ON CONFLICT(Id) DO UPDATE SET RootId=$root,Title=$title,Artist=$artist,DurationTicks=$duration,Capabilities=$capabilities;";
            song.Parameters.AddWithValue("$id", songId.ToString());
            song.Parameters.AddWithValue("$root", rootId.ToString());
            song.Parameters.AddWithValue("$title", metadata.Title);
            song.Parameters.AddWithValue("$artist", (object?)metadata.Artist ?? DBNull.Value);
            song.Parameters.AddWithValue("$duration", metadata.Duration is null ? DBNull.Value : metadata.Duration.Value.Ticks);
            song.Parameters.AddWithValue("$capabilities", (int)metadata.Capabilities);
            await song.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var indexedFile = connection.CreateCommand())
        {
            indexedFile.Transaction = (SqliteTransaction)transaction;
            indexedFile.CommandText = "INSERT INTO Files(Path,SongId,Size,LastWriteTimeUtc,ProviderId,ProviderVersion) VALUES($path,$song,$size,$written,$provider,$version) ON CONFLICT(Path) DO UPDATE SET SongId=$song,Size=$size,LastWriteTimeUtc=$written,ProviderId=$provider,ProviderVersion=$version;";
            indexedFile.Parameters.AddWithValue("$path", path);
            indexedFile.Parameters.AddWithValue("$song", songId.ToString());
            indexedFile.Parameters.AddWithValue("$size", file.Size);
            indexedFile.Parameters.AddWithValue("$written", file.LastWriteTimeUtc.ToString("O"));
            indexedFile.Parameters.AddWithValue("$provider", providerId);
            indexedFile.Parameters.AddWithValue("$version", providerVersion);
            await indexedFile.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var search = connection.CreateCommand())
        {
            search.Transaction = (SqliteTransaction)transaction;
            search.CommandText = "DELETE FROM SongSearch WHERE SongId=$id; INSERT INTO SongSearch(SongId,Title,Artist) VALUES($id,$title,$artist);";
            search.Parameters.AddWithValue("$id", songId.ToString());
            search.Parameters.AddWithValue("$title", metadata.Title);
            search.Parameters.AddWithValue("$artist", metadata.Artist ?? "");
            await search.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return songId;
    }

    public async ValueTask MarkFileSeenAsync(
        string path,
        Guid scanId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Files SET LastSeenScanId=$scan WHERE Path=$path;";
        command.Parameters.AddWithValue("$scan", scanId.ToString());
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask RemoveFilesNotSeenAsync(
        Guid rootId,
        Guid scanId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DELETE FROM SongSearch WHERE SongId IN (SELECT s.Id FROM Songs s JOIN Files f ON f.SongId=s.Id WHERE s.RootId=$root AND (f.LastSeenScanId IS NULL OR f.LastSeenScanId<>$scan)); DELETE FROM Songs WHERE RootId=$root AND Id IN (SELECT SongId FROM Files WHERE LastSeenScanId IS NULL OR LastSeenScanId<>$scan);";
        command.Parameters.AddWithValue("$root", rootId.ToString());
        command.Parameters.AddWithValue("$scan", scanId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async ValueTask<Guid?> FindSongIdAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string path,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT SongId FROM Files WHERE Path=$path;";
        command.Parameters.AddWithValue("$path", path);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string id ? Guid.Parse(id) : null;
    }

    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string ToFtsQuery(string text) => string.Join(
        " AND ",
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => $"\"{term.Replace("\"", "\"\"")}\"*"));

    private static async ValueTask<object?> ExecuteScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async ValueTask ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
