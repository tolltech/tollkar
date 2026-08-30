using Microsoft.Data.Sqlite;
using Tollkar.Application.Queue.Models;
using Tollkar.Application.Queue.Persistence;

namespace Tollkar.Infrastructure.Queue;

internal sealed class SqlitePlaybackQueueRepository(string databasePath) : IPlaybackQueueRepository
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath),
        ForeignKeys = true
    }.ToString();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async ValueTask<IReadOnlyList<PlaybackQueueItem>> GetItemsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var items = new List<PlaybackQueueItem>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT q.Id,q.SongId,s.Title,s.Artist,q.Position,q.UserId FROM PlaybackQueue q JOIN Songs s ON s.Id=q.SongId WHERE q.UserId=$user ORDER BY q.Position;";
        command.Parameters.AddWithValue("$user", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4), reader.GetString(5)));
        }

        return items;
    }

    public async ValueTask AddAsync(
        string userId,
        Guid queueItemId,
        Guid songId,
        CancellationToken cancellationToken = default)
    {
        await InWriteLockAsync(async () =>
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO PlaybackQueue(Id,SongId,Position,UserId) SELECT $id,$song,COALESCE(MAX(Position),-1)+1,$user FROM PlaybackQueue WHERE UserId=$user;";
            command.Parameters.AddWithValue("$user", userId);
            command.Parameters.AddWithValue("$id", queueItemId.ToString());
            command.Parameters.AddWithValue("$song", songId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async ValueTask RemoveAsync(string userId, Guid queueItemId, CancellationToken cancellationToken = default)
    {
        await InWriteLockAsync(async () =>
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "DELETE FROM PlaybackQueue WHERE Id=$id AND UserId=$user; UPDATE PlaybackQueue SET Position=(SELECT COUNT(*) FROM PlaybackQueue before WHERE before.UserId=$user AND before.Position < PlaybackQueue.Position) WHERE UserId=$user;";
            command.Parameters.AddWithValue("$user", userId);
            command.Parameters.AddWithValue("$id", queueItemId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);
    }

    public async ValueTask MoveByAsync(
        string userId,
        Guid queueItemId,
        int offset,
        CancellationToken cancellationToken = default)
    {
        await InWriteLockAsync(async () =>
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var sqliteTransaction = (SqliteTransaction)transaction;
            var currentPosition = await GetPositionAsync(connection, sqliteTransaction, userId, queueItemId, cancellationToken);
            if (currentPosition is null) return;
            var count = Convert.ToInt32(await ExecuteScalarAsync(connection, sqliteTransaction, userId, "SELECT COUNT(*) FROM PlaybackQueue WHERE UserId=$user;", cancellationToken));
            var target = (int)Math.Clamp(
                (long)currentPosition.Value + offset,
                0,
                count - 1L);
            if (target == currentPosition) return;

            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = target < currentPosition
                ? "UPDATE PlaybackQueue SET Position=Position+1 WHERE UserId=$user AND Position >= $target AND Position < $current; UPDATE PlaybackQueue SET Position=$target WHERE Id=$id AND UserId=$user;"
                : "UPDATE PlaybackQueue SET Position=Position-1 WHERE UserId=$user AND Position > $current AND Position <= $target; UPDATE PlaybackQueue SET Position=$target WHERE Id=$id AND UserId=$user;";
            command.Parameters.AddWithValue("$user", userId);
            command.Parameters.AddWithValue("$id", queueItemId.ToString());
            command.Parameters.AddWithValue("$current", currentPosition.Value);
            command.Parameters.AddWithValue("$target", target);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);
    }

    private async ValueTask InWriteLockAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async ValueTask<int?> GetPositionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string userId,
        Guid queueItemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Position FROM PlaybackQueue WHERE Id=$id AND UserId=$user;";
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$id", queueItemId.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? null : Convert.ToInt32(result);
    }

    private static async ValueTask<object?> ExecuteScalarAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string userId,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user", userId);
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
