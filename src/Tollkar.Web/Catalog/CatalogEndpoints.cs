using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Tollkar.Application.Library;
using Tollkar.Application.Library.Models;
using Tollkar.Application.Queue;
using Tollkar.Core.Formats.Kfn;
using Tollkar.Core.Formats.Video;
using Tollkar.Web.Authentication;
using Tollkar.Web.Logging;
using Tollkar.Web.Media;
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

        app.MapGet("/api/admin/songs", async (string? text, int? limit,
            ILibraryService library, CancellationToken cancellationToken) =>
        {
            if (limit is < 1 or > LibrarySearchQuery.MaximumLimit)
                return Invalid("Limit", "Лимит должен быть от 1 до 500.");

            var query = new LibrarySearchQuery(text, limit ?? LibrarySearchQuery.MaximumLimit);
            var counts = await library.GetSongCountsAsync(text, cancellationToken);
            var songs = await library.SearchSongsAsync(query, cancellationToken);
            return Results.Ok(new AdminSongCatalogResponse(songs, counts.TotalCount, counts.MatchedCount));
        }).RequireAuthorization(AdministratorAccount.PolicyName);

        app.MapDelete("/api/admin/songs/{songId:guid}", DeleteSongAsync)
            .LogUserAction()
            .RequireAuthorization(AdministratorAccount.PolicyName)
            .AddEndpointFilter<ValidateAuthRequest>();

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
        }).LogUserAction().SuppressAutomaticPlaybackLogging().AddEndpointFilter<ValidateAuthRequest>();
        queue.MapPost("/", AddAsync).LogUserAction().AddEndpointFilter<ValidateAuthRequest>();
        queue.MapDelete("/", async (SynchronizedPlaybackQueue service,
            CancellationToken cancellationToken) =>
        {
            await service.ClearAsync(cancellationToken);
            return Results.NoContent();
        }).LogUserAction().AddEndpointFilter<ValidateAuthRequest>();
        queue.MapPost("/{id:guid}/play", async (Guid id, SynchronizedPlaybackQueue service,
            CancellationToken cancellationToken) =>
        {
            if (id == Guid.Empty) return Invalid("Id", "Укажите элемент очереди.");
            await service.PlayNowAsync(id, cancellationToken);
            return Results.NoContent();
        }).LogUserAction().AddEndpointFilter<ValidateAuthRequest>();
        queue.MapDelete("/{id:guid}", async (Guid id, IPlaybackQueueService service,
            CancellationToken cancellationToken) =>
        {
            if (id == Guid.Empty) return Invalid("Id", "Укажите элемент очереди.");
            await service.RemoveAsync(id, cancellationToken);
            return Results.NoContent();
        }).LogUserAction().AddEndpointFilter<ValidateAuthRequest>();
        queue.MapPost("/{id:guid}/move", async (Guid id, MoveSong request,
            IPlaybackQueueService service, CancellationToken cancellationToken) =>
        {
            if (id == Guid.Empty) return Invalid("Id", "Укажите элемент очереди.");
            await service.MoveByAsync(id, request.Offset, cancellationToken);
            return Results.NoContent();
        }).LogUserAction().AddEndpointFilter<ValidateAuthRequest>();
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

    private static async Task<IResult> DeleteSongAsync(Guid songId,
        ILibraryService library,
        IHostEnvironment environment,
        IOptions<LibrarySyncOptions> options,
        CancellationToken cancellationToken)
    {
        var song = songId == Guid.Empty ? null : await library.GetSongAsync(songId, cancellationToken);
        if (song is null) return Results.NotFound();

        var extension = song.Source.ProviderId switch
        {
            KfnSongFormatProvider.ProviderId => KfnSongFormatProvider.Extension,
            VideoSongFormatProvider.ProviderId => VideoSongFormatProvider.Extension,
            _ => null
        };
        var root = Path.GetFullPath(options.Value.SongsPath, environment.ContentRootPath);
        var path = extension is null ? null : SongMediaFile.Locate(root, song.Source.FilePath, extension);
        if (path is null) return Results.NotFound();

        try
        {
            File.Delete(path);
            return Results.NoContent();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Results.Problem("Не удалось удалить файл песни.", statusCode: StatusCodes.Status409Conflict);
        }
    }

    public sealed record AdminSongCatalogResponse(
        IReadOnlyList<LibrarySong> Items,
        int TotalCount,
        int MatchedCount);
}
