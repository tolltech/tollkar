using System.Text;

namespace Tollkar.Core.Formats.Kfn;

/// <summary>
/// KFN mixes encodings inside a single file: entry names and file references are Windows-1251,
/// while titles and lyrics are UTF-8. Only decoding is ever required, so the code page is a
/// lookup table rather than a dependency on System.Text.Encoding.CodePages, which would also
/// force a process-wide Encoding.RegisterProvider call from a library.
/// The encoding is told apart by trying UTF-8 first, which a Cyrillic Windows-1251 string of any
/// length practically never satisfies; a very short one could in principle, and would then be
/// read as mojibake.
/// </summary>
internal static class KfnText
{
    /// <summary>Windows-1251 characters for bytes 0x80..0xFF; 0x98 is unassigned.</summary>
    private const string Windows1251HighRange =
        "ЂЃ‚ѓ„…†‡€‰Љ‹ЊЌЋЏ" +
        "ђ‘’“”•–—�™љ›њќћџ" +
        " ЎўЈ¤Ґ¦§Ё©Є«¬­®Ї" +
        "°±Ііґµ¶·ё№є»јЅѕї" +
        "АБВГДЕЖЗИЙКЛМНОП" +
        "РСТУФХЦЧШЩЪЫЬЭЮЯ" +
        "абвгдежзийклмноп" +
        "рстуфхцчшщъыьэюя";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Title and artist fields are routinely filled with placeholders such as "-", which say
    /// no more than an empty field and must not win over the file name or folder.
    /// </summary>
    public static string? Meaningful(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || !trimmed.Any(char.IsLetterOrDigit)
            ? null
            : trimmed;
    }

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return DecodeWindows1251(bytes);
        }
    }

    private static string DecodeWindows1251(ReadOnlySpan<byte> bytes)
    {
        var characters = new char[bytes.Length];
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            characters[index] = value < 0x80
                ? (char)value
                : Windows1251HighRange[value - 0x80];
        }

        return new string(characters);
    }
}
