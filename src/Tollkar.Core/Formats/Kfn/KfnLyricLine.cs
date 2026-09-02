namespace Tollkar.Core.Formats.Kfn;

public sealed record KfnLyricLine
{
    public KfnLyricLine(IReadOnlyList<KfnSyllable> syllables)
    {
        ArgumentNullException.ThrowIfNull(syllables);
        if (syllables.Count == 0)
        {
            throw new ArgumentException("A lyric line needs at least one syllable.", nameof(syllables));
        }

        Syllables = syllables;
    }

    public IReadOnlyList<KfnSyllable> Syllables { get; }

    public int StartMs => Syllables[0].StartMs;

    public int EndMs => Syllables[^1].EndMs;
}
