namespace Tollkar.Core.Formats.Kfn;

/// <param name="Text">Includes the trailing space when the syllable ends a word, so that
/// concatenating a line's syllables reproduces the original line.</param>
public sealed record KfnSyllable(string Text, int StartMs, int EndMs);
