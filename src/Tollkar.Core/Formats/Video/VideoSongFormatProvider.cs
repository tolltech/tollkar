using Tollkar.Core.Songs;

namespace Tollkar.Core.Formats.Video;

public sealed class VideoSongFormatProvider : ISongFormatProvider
{
    public const string ProviderId = "video";
    public const string Extension = ".mp4";

    private const string ArtistTitleSeparator = " - ";

    public string Id => ProviderId;

    public int Version => 1;

    public int Priority => 0;

    public bool CanHandle(FileCandidate file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return string.Equals(
            Path.GetExtension(file.Path),
            Extension,
            StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<SongMetadata> ReadMetadataAsync(
        FileCandidate file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();

        var fileName = Path.GetFileNameWithoutExtension(file.Path).Trim();
        if (fileName.Length == 0)
        {
            throw new InvalidDataException(
                $"Cannot determine a song title from '{file.Path}'.");
        }

        var (artist, title) = ParseArtistAndTitle(fileName);
        var metadata = new SongMetadata(
            title,
            artist,
            duration: null,
            SongCapabilities.Audio | SongCapabilities.Video);

        return ValueTask.FromResult(metadata);
    }

    private static (string? Artist, string Title) ParseArtistAndTitle(string fileName)
    {
        var separatorIndex = fileName.IndexOf(
            ArtistTitleSeparator,
            StringComparison.Ordinal);

        if (separatorIndex <= 0)
        {
            return (null, fileName);
        }

        var titleIndex = separatorIndex + ArtistTitleSeparator.Length;
        if (titleIndex >= fileName.Length)
        {
            return (null, fileName);
        }

        var artist = fileName[..separatorIndex].Trim();
        var title = fileName[titleIndex..].Trim();

        return artist.Length > 0 && title.Length > 0
            ? (artist, title)
            : (null, fileName);
    }
}
