using Microsoft.Extensions.Options;
using Tollkar.Application.Library;
using Tollkar.Web.Catalog;

namespace Tollkar.Web.Media;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        app.MapMethods("/api/songs/{songId:guid}/media", ["GET", "HEAD"], StreamAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> StreamAsync(Guid songId, ILibraryService library,
        IHostEnvironment environment, IOptions<LibrarySyncOptions> options,
        HttpContext context, CancellationToken cancellationToken)
    {
        if (songId == Guid.Empty) return Results.NotFound();
        var song = await library.GetSongAsync(songId, cancellationToken);
        if (song is null || song.Source.ProviderId != "video") return Results.NotFound();

        var root = Path.GetFullPath(options.Value.SongsPath, environment.ContentRootPath);
        var stream = SongMediaFile.Open(root, song.Source.FilePath);
        if (stream is null) return Results.NotFound();

        context.Response.Headers.XContentTypeOptions = "nosniff";
        // The video provider currently indexes only MP4. Do not serve arbitrary catalog file types.
        return Results.Stream(stream, "video/mp4", enableRangeProcessing: true);
    }
}
