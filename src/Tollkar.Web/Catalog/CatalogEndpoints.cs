using Microsoft.Data.Sqlite;
using Tollkar.Application.Library;
using Tollkar.Application.Library.Models;
using Tollkar.Application.Queue;
using Tollkar.Web.Authentication;
using Tollkar.Web.Realtime;

namespace Tollkar.Web.Catalog;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        app.MapGet("/api/library/search", async (string? text, int? limit,
            ILibraryService library, CancellationToken cancellationToken) =>
        {
            if (limit is < 1 or > LibrarySearchQuery.MaximumLimit)
                return Invalid("Limit", "Лимит должен быть от 1 до 500.");
            return Results.Ok(await library.SearchSongsAsync(new(text, limit ?? 100), cancellationToken));
        }).RequireAuthorization();

        var queue = app.MapGroup("/api/queue").RequireAuthorization();
        queue.MapGet("/", async (IPlaybackQueueService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetItemsAsync(cancellationToken)));
        queue.MapPost("/playback", async (PlaybackCommand request, SynchronizedPlaybackQueue service,
            CancellationToken cancellationToken) =>
        {
            if (request.Action is not ("play" or "pause" or "seek" or "next" or "ended")
                || request.Revision < 0 || !double.IsFinite(request.PositionSeconds)
                || request.PositionSeconds < 0 || request.PositionSeconds > 86400)
                return Invalid("Playback", "Некорректная команда воспроизведения.");
            await service.ControlAsync(request, cancellationToken);
            return Results.NoContent();
        }).AddEndpointFilter<ValidateAuthRequest>();
        queue.MapPost("/", AddAsync).AddEndpointFilter<ValidateAuthRequest>();
        queue.MapPost("/{id:guid}/play", async (Guid id, SynchronizedPlaybackQueue service,
            CancellationToken cancellationToken) =>
        {
            if (id == Guid.Empty) return Invalid("Id", "Укажите элемент очереди.");
            await service.PlayNowAsync(id, cancellationToken);
            return Results.NoContent();
        }).AddEndpointFilter<ValidateAuthRequest>();
        queue.MapDelete("/{id:guid}", async (Guid id, IPlaybackQueueService service,
            CancellationToken cancellationToken) =>
        {
            if (id == Guid.Empty) return Invalid("Id", "Укажите элемент очереди.");
            await service.RemoveAsync(id, cancellationToken);
            return Results.NoContent();
        }).AddEndpointFilter<ValidateAuthRequest>();
        queue.MapPost("/{id:guid}/move", async (Guid id, MoveSong request,
            IPlaybackQueueService service, CancellationToken cancellationToken) =>
        {
            if (id == Guid.Empty) return Invalid("Id", "Укажите элемент очереди.");
            await service.MoveByAsync(id, request.Offset, cancellationToken);
            return Results.NoContent();
        }).AddEndpointFilter<ValidateAuthRequest>();
    }

    private static async Task<IResult> AddAsync(AddSong request, ILibraryService library,
        IPlaybackQueueService queue, CancellationToken cancellationToken)
    {
        if (request.SongId == Guid.Empty) return Invalid("SongId", "Укажите песню.");
        if (await library.GetSongAsync(request.SongId, cancellationToken) is null)
            return Results.NotFound();
        try
        {
            await queue.AddAsync(request.SongId, cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 787)
        {
            // A library refresh can remove the song between lookup and insertion.
            return Results.NotFound();
        }
        return Results.NoContent();
    }

    private static IResult Invalid(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    public sealed record AddSong(Guid SongId);
    public sealed record MoveSong(int Offset);
}
