using System.Globalization;
using System.Text;

namespace Tollkar.Core.Formats.Kfn;

/// <summary>
/// Parses Song.ini, the script that ties a container's payloads together: which track to play,
/// which clip to show behind it and when every syllable is sung.
/// </summary>
public sealed class KfnSongDefinition
{
    /// <summary>Sync marks are start times only; a held syllable must not keep the highlight
    /// running across an instrumental break, so its duration is capped.</summary>
    private const int MaximumSyllableMs = 1000;
    private const int CentisecondMs = 10;
    /// <summary>Twenty-four hours in centiseconds; anything beyond is a corrupt mark.</summary>
    private const int MaximumSyncMark = 8_640_000;
    private const char SectionOpen = '[';
    private const char SectionClose = ']';
    private const string GeneralSection = "General";
    private const string UseMusicSource = "UseMusicSource";

    private KfnSongDefinition(
        string? title,
        string? artist,
        string? audioFileName,
        string? backgroundFileName,
        bool loopBackground,
        IReadOnlyList<KfnLyricLine> lines)
    {
        Title = title;
        Artist = artist;
        AudioFileName = audioFileName;
        BackgroundFileName = backgroundFileName;
        LoopBackground = loopBackground;
        Lines = lines;
    }

    public string? Title { get; }

    public string? Artist { get; }

    /// <summary>The track named by <c>Source</c>; matters for files carrying both a backing
    /// track and a guide vocal.</summary>
    public string? AudioFileName { get; }

    public string? BackgroundFileName { get; }

    public bool LoopBackground { get; }

    public IReadOnlyList<KfnLyricLine> Lines { get; }

    public static KfnSongDefinition Parse(ReadOnlySpan<byte> content)
    {
        var sections = ReadSections(content);
        var general = sections.FirstOrDefault(section => section.Name == GeneralSection);
        var background = sections.FirstOrDefault(section =>
            !string.IsNullOrWhiteSpace(section.Value("VideoFile")) &&
            section.Value("VideoFile") != UseMusicSource);
        var lyrics = sections.FirstOrDefault(section => section.Texts.Count > 0);

        return new KfnSongDefinition(
            KfnText.Meaningful(general?.Value("Title")),
            KfnText.Meaningful(general?.Value("Artist")),
            ParseAudioFileName(general?.Value("Source")),
            Trim(background?.Value("VideoFile")),
            background?.Value("LoopVideo") == "1",
            lyrics is null ? [] : BuildLines(lyrics, Shift(general?.Value("GlobalShift"))));
    }

    /// <summary><c>Source</c> is written as "1,I,&lt;file name&gt;".</summary>
    private static string? ParseAudioFileName(string? source)
    {
        if (source is null) return null;
        var separator = source.IndexOf(',');
        if (separator < 0) return Trim(source);
        separator = source.IndexOf(',', separator + 1);
        return separator < 0 ? Trim(source) : Trim(source[(separator + 1)..]);
    }

    private static int Shift(string? globalShift) =>
        int.TryParse(globalShift, CultureInfo.InvariantCulture, out var value)
            ? value * CentisecondMs
            : 0;

    private static IReadOnlyList<KfnLyricLine> BuildLines(IniSection lyrics, int shiftMs)
    {
        var marks = lyrics.SyncMarks();
        var lines = new List<KfnLyricLine>();
        var mark = 0;

        foreach (var text in lyrics.Texts)
        {
            var tokens = Tokenize(text);
            if (tokens.Count == 0) continue;
            // Sync marks run out before the tokens in files with a damaged script; the song
            // still plays, so keep the lines that are timed instead of failing the whole file.
            if (mark + tokens.Count > marks.Count) break;

            var syllables = new List<KfnSyllable>(tokens.Count);
            for (var index = 0; index < tokens.Count; index++, mark++)
            {
                var start = Math.Max(0, marks[mark] * CentisecondMs + shiftMs);
                var next = mark + 1 < marks.Count
                    ? Math.Max(0, marks[mark + 1] * CentisecondMs + shiftMs)
                    : int.MaxValue;
                syllables.Add(new KfnSyllable(
                    tokens[index],
                    start,
                    Math.Max(start, Math.Min(next, start + MaximumSyllableMs))));
            }

            lines.Add(new KfnLyricLine(syllables));
        }

        return lines;
    }

    /// <summary>Splits a line into sung tokens. A syllable that ends a word keeps its trailing
    /// space so the highlight sweeps across the gap and the line renders unchanged.</summary>
    private static IReadOnlyList<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var character in line)
        {
            if (character is not ('/' or ' ' or '\t'))
            {
                current.Append(character);
                continue;
            }

            if (current.Length == 0) continue;
            if (character != '/') current.Append(' ');
            tokens.Add(current.ToString());
            current.Clear();
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private static IReadOnlyList<IniSection> ReadSections(ReadOnlySpan<byte> content)
    {
        var sections = new List<IniSection>();
        var current = new IniSection(string.Empty);
        sections.Add(current);

        foreach (var range in SplitLines(content))
        {
            var line = content[range];
            if (line.Length == 0) continue;

            if (line[0] == SectionOpen)
            {
                var name = KfnText.Decode(line).Trim();
                current = new IniSection(name.TrimStart(SectionOpen).TrimEnd(SectionClose));
                sections.Add(current);
                continue;
            }

            var separator = line.IndexOf((byte)'=');
            if (separator < 0) continue;
            current.Add(
                KfnText.Decode(line[..separator]).Trim(),
                KfnText.Decode(line[(separator + 1)..]).Trim());
        }

        return sections;
    }

    private static List<Range> SplitLines(ReadOnlySpan<byte> content)
    {
        var ranges = new List<Range>();
        var start = 0;
        for (var index = 0; index <= content.Length; index++)
        {
            if (index != content.Length && content[index] != (byte)'\n') continue;
            var end = index > start && content[index - 1] == (byte)'\r' ? index - 1 : index;
            ranges.Add(start..end);
            start = index + 1;
        }

        return ranges;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class IniSection(string name)
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly SortedDictionary<int, string> _texts = [];
        private readonly SortedDictionary<int, string> _syncs = [];

        public string Name { get; } = name;

        public IReadOnlyCollection<string> Texts => _texts.Values;

        public void Add(string key, string value)
        {
            if (Index(key, "Text") is { } textIndex) _texts[textIndex] = value;
            else if (Index(key, "Sync") is { } syncIndex) _syncs[syncIndex] = value;
            else _values[key] = value;
        }

        public string? Value(string key) => _values.GetValueOrDefault(key);

        /// <summary>The sync marks of a song are split across numbered keys purely for line
        /// length; they form one flat sequence aligned with the syllables.</summary>
        public IReadOnlyList<int> SyncMarks() =>
        [
            .. _syncs.Values
                .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(mark => int.TryParse(mark, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : -1)
                .Where(mark => mark is >= 0 and <= MaximumSyncMark)
        ];

        private static int? Index(string key, string prefix) =>
            key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key.AsSpan(prefix.Length), CultureInfo.InvariantCulture, out var index)
                ? index
                : null;
    }
}
