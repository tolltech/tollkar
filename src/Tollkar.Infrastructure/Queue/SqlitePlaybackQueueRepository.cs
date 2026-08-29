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
        CancellationToken cancellationToken = default)
    {
        var items = new List<PlaybackQueueItem>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT q.Id,q.SongId,s.Title,s.Artist,q.Position FROM PlaybackQueue q JOIN Songs s ON s.Id=q.SongId ORDER BY q.Position;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4)));
        }

        return items;
    }

    public async ValueTask AddAsync(
        Guid queueItemId,
        Guid songId,
        CancellationToken cancellationToken = default)
    {
        await InWriteLockAsync(async () =>
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO PlaybackQueue(Id,SongId,Position) SELECT $id,$song,COALESCE(MAX(Position),-1)+1 FROM PlaybackQueue;";
            command.Parameters.AddWithValue("$id", queueItemId.ToString());
            command.Parameters.AddWithValue("$song", songId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async ValueTask RemoveAsync(Guid queueItemId, CancellationToken cancellationToken = default)
    {
        await InWriteLockAsync(async () =>
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "DELETE FROM PlaybackQueue WHERE Id=$id; UPDATE PlaybackQueue SET Position=(SELECT COUNT(*) FROM PlaybackQueue before WHERE before.Position < PlaybackQueue.Position);";
            command.Parameters.AddWithValue("$id", queueItemId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);
    }

    public async ValueTask MoveByAsync(
        Guid queueItemId,
        int offset,
        CancellationToken cancellationToken = default)
    {
        await InWriteLockAsync(async () =>
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var sqliteTransaction = (SqliteTransaction)transaction;
            var currentPosition = await GetPositionAsync(connection, sqliteTransaction, queueItemId, cancellationToken);
            if (currentPosition is null) return;
            var count = Convert.ToInt32(await ExecuteScalarAsync(connection, sqliteTransaction, "SELECT COUNT(*) FROM PlaybackQueue;", cancellationToken));
            var target = (int)Math.Clamp(
                (long)currentPosition.Value + offset,
                0,
                count - 1L);
            if (target == currentPosition) return;

            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = target < currentPosition
                ? "UPDATE PlaybackQueue SET Position=Position+1 WHERE Position >= $target AND Position < $current; UPDATE PlaybackQueue SET Position=$target WHERE Id=$id;"
                : "UPDATE PlaybackQueue SET Position=Position-1 WHERE Position > $current AND Position <= $target; UPDATE PlaybackQueue SET Position=$target WHERE Id=$id;";
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
        Guid queueItemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Position FROM PlaybackQueue WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", queueItemId.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? null : Convert.ToInt32(result);
    }

    private static async ValueTask<object?> ExecuteScalarAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
