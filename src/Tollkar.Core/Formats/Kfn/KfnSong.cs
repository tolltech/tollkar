namespace Tollkar.Core.Formats.Kfn;

/// <summary>
/// A KFN container read as one song: the track to play, the clip to show behind it and the
/// timed lyrics. Resolving those from the raw entries lives here so the catalog scanner and the
/// web endpoints agree on what a given file offers.
/// </summary>
public sealed class KfnSong
{
    private const string Mp4BoxType = "ftyp";
    private const int Mp4BoxTypeOffset = 4;

    private readonly KfnArchive _archive;
    private readonly KfnEntry? _audio;
    // Resolving the background reads from the file to check its container, which callers that
    // only stream the track never need.
    private readonly Lazy<KfnEntry?> _background;

    private KfnSong(KfnArchive archive, KfnSongDefinition definition, KfnEntry? audio)
    {
        _archive = archive;
        _audio = audio;
        _background = new Lazy<KfnEntry?>(() => FindBackground(archive, definition));
        Title = archive.Title ?? definition.Title;
        Artist = archive.Artist ?? definition.Artist;
        Lines = definition.Lines;
        LoopBackground = definition.LoopBackground;
    }

    public string? Title { get; }

    public string? Artist { get; }

    public IReadOnlyList<KfnLyricLine> Lines { get; }

    public bool LoopBackground { get; }

    public bool HasAudio => _audio is not null;

    public bool HasBackground => _background.Value is not null;

    public static KfnSong Open(string path)
    {
        var archive = KfnArchive.Open(path);
        var definitionEntry = archive.FirstEntry(KfnEntryKind.SongDefinition)
            ?? throw new InvalidDataException($"'{path}' contains no song definition.");
        var definition = KfnSongDefinition.Parse(archive.ReadEntry(definitionEntry));

        return new KfnSong(archive, definition, FindAudio(archive, definition));
    }

    public Stream? OpenAudio() => _audio is null ? null : _archive.OpenEntry(_audio);

    public Stream? OpenBackground() =>
        _background.Value is null ? null : _archive.OpenEntry(_background.Value);

    private static KfnEntry? FindAudio(KfnArchive archive, KfnSongDefinition definition) =>
        (definition.AudioFileName is null
            ? null
            : archive.FindEntry(definition.AudioFileName, KfnEntryKind.Audio))
        ?? archive.FirstEntry(KfnEntryKind.Audio);

    /// <summary>
    /// Only the clip named by the script counts as a background, and only when it really is an
    /// MP4. Containers routinely give an MP4 an ".avi" name, and the few genuine AVI clips play
    /// nowhere in a browser, so they are treated as no background at all.
    /// </summary>
    private static KfnEntry? FindBackground(KfnArchive archive, KfnSongDefinition definition)
    {
        if (definition.BackgroundFileName is null) return null;
        var entry = archive.FindEntry(definition.BackgroundFileName, KfnEntryKind.Video);
        return entry is not null && IsMp4(archive, entry) ? entry : null;
    }

    private static bool IsMp4(KfnArchive archive, KfnEntry entry)
    {
        var boxType = new byte[Mp4BoxType.Length];
        using var stream = archive.OpenEntry(entry);
        stream.Position = Mp4BoxTypeOffset;
        return stream.ReadAtLeast(boxType, boxType.Length, throwOnEndOfStream: false) == boxType.Length
            && KfnText.Decode(boxType) == Mp4BoxType;
    }
}
