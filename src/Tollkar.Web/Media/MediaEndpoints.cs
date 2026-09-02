using Microsoft.Extensions.Options;
using Tollkar.Application.Library;
using Tollkar.Core.Formats.Kfn;
using Tollkar.Core.Formats.Video;
using Tollkar.Core.Songs;
using Tollkar.Web.Catalog;

namespace Tollkar.Web.Media;

public static class MediaEndpoints
{
    private const string VideoContentType = "video/mp4";
    private const string AudioContentType = "audio/mpeg";

    public static void MapMediaEndpoints(this WebApplication app)
    {
        var songs = app.MapGroup("/api/songs/{songId:guid}").RequireAuthorization();
        songs.MapMethods("/media", ["GET", "HEAD"], StreamAsync);
        songs.MapMethods("/background", ["GET", "HEAD"], StreamBackgroundAsync);
        songs.MapGet("/karaoke", ReadKaraokeAsync);
    }

    private static async Task<IResult> StreamAsync(Guid songId, ILibraryService library,
        IHostEnvironment environment, IOptions<LibrarySyncOptions> options,
        HttpContext context, CancellationToken cancellationToken)
    {
        var song = await FindSongAsync(songId, library, cancellationToken);
        if (song is null) return Results.NotFound();

        var root = Root(environment, options);
        // The video provider indexes only MP4, and a karaoke container always plays its MP3.
        // Do not serve arbitrary catalog file types.
        var media = song.Source.ProviderId switch
        {
            VideoSongFormatProvider.ProviderId => Media(
                SongMediaFile.Open(root, song.Source.FilePath, VideoSongFormatProvider.Extension),
                VideoContentType),
            KfnSongFormatProvider.ProviderId => Media(OpenKaraoke(root, song)?.OpenAudio(), AudioContentType),
            _ => null
        };

        return media is null ? Results.NotFound() : Streamed(context, media.Value);
    }

    private static async Task<IResult> StreamBackgroundAsync(Guid songId, ILibraryService library,
        IHostEnvironment environment, IOptions<LibrarySyncOptions> options,
        HttpContext context, CancellationToken cancellationToken)
    {
        var song = await FindSongAsync(songId, library, cancellationToken);
        if (song is null || song.Source.ProviderId != KfnSongFormatProvider.ProviderId) return Results.NotFound();

        // A karaoke background is only ever exposed when it is an MP4 the browser can play.
        var background = OpenKaraoke(Root(environment, options), song)?.OpenBackground();
        return background is null
            ? Results.NotFound()
            : Streamed(context, (background, VideoContentType));
    }

    private static async Task<IResult> ReadKaraokeAsync(Guid songId, ILibraryService library,
        IHostEnvironment environment, IOptions<LibrarySyncOptions> options,
        CancellationToken cancellationToken)
    {
        var song = await FindSongAsync(songId, library, cancellationToken);
        if (song is null || song.Source.ProviderId != KfnSongFormatProvider.ProviderId) return Results.NotFound();

        var karaoke = OpenKaraoke(Root(environment, options), song);
        if (karaoke is null) return Results.NotFound();

        return Results.Ok(new KaraokeScript(
            karaoke.HasBackground ? new KaraokeBackground(karaoke.LoopBackground) : null,
            karaoke.Lines));
    }

    private static async Task<Song?> FindSongAsync(Guid songId, ILibraryService library,
        CancellationToken cancellationToken) =>
        songId == Guid.Empty ? null : await library.GetSongAsync(songId, cancellationToken);

    private static KfnSong? OpenKaraoke(string root, Song song)
    {
        var path = SongMediaFile.Locate(root, song.Source.FilePath, KfnSongFormatProvider.Extension);
        if (path is null) return null;

        try
        {
            return KfnSong.Open(path);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
                                        or UnauthorizedAccessException)
        {
            // A damaged or half-copied container must read like a missing song, not a failure.
            return null;
        }
    }

    private static string Root(IHostEnvironment environment, IOptions<LibrarySyncOptions> options) =>
        Path.GetFullPath(options.Value.SongsPath, environment.ContentRootPath);

    private static (Stream Content, string ContentType)? Media(Stream? content, string contentType) =>
        content is null ? null : (content, contentType);

    private static IResult Streamed(HttpContext context, (Stream Content, string ContentType) media)
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Stream(media.Content, media.ContentType, enableRangeProcessing: true);
    }
}
