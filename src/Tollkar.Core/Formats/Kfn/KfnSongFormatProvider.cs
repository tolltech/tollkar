using Tollkar.Core.Songs;

namespace Tollkar.Core.Formats.Kfn;

public sealed class KfnSongFormatProvider : ISongFormatProvider
{
    public const string ProviderId = "kfn";
    public const string Extension = ".kfn";

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

        var song = KfnSong.Open(file.Path);
        var metadata = new SongMetadata(
            song.Title ?? FileName(file.Path),
            song.Artist ?? ContainingFolder(file.Path),
            duration: null,
            Capabilities(song));

        return ValueTask.FromResult(metadata);
    }

    private static SongCapabilities Capabilities(KfnSong song)
    {
        var capabilities = SongCapabilities.None;
        if (song.HasAudio) capabilities |= SongCapabilities.Audio;
        if (song.HasBackground) capabilities |= SongCapabilities.Video;
        if (song.Lines.Count > 0) capabilities |= SongCapabilities.SyncedLyrics;
        return capabilities;
    }

    private static string FileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Trim();
        return name.Length > 0
            ? name
            : throw new InvalidDataException($"Cannot determine a song title from '{path}'.");
    }

    /// <summary>KFN files carry only the title in their name, so the folder they are filed
    /// under is the last hint about the artist.</summary>
    private static string? ContainingFolder(string path)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(path))?.Trim();
        return string.IsNullOrEmpty(folder) ? null : folder;
    }
}
