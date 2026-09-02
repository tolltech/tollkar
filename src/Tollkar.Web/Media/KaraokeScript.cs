using Tollkar.Core.Formats.Kfn;

namespace Tollkar.Web.Media;

/// <summary>Everything the player needs beyond the audio stream: whether a background clip is
/// available and when each syllable is sung.</summary>
public sealed record KaraokeScript(
    KaraokeBackground? Background,
    IReadOnlyList<KfnLyricLine> Lines);

public sealed record KaraokeBackground(bool Loop);
