using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Tollkar.TestSupport;

/// <summary>
/// Writes synthetic KFN containers so the parser and the media endpoints are covered without
/// carrying multi-megabyte karaoke files in the repository.
/// </summary>
public sealed class KfnFileBuilder
{
    private readonly List<(string Name, int Kind, byte[] Content, bool Encrypt)> _entries = [];
    private readonly List<(string Field, byte[] Value)> _fields = [];
    private readonly HashSet<string> _malformedEncryption = [];
    private byte[]? _fileKey;

    public KfnFileBuilder WithTitle(string title) => WithField("TITL", Encoding.UTF8.GetBytes(title));

    public KfnFileBuilder WithArtist(string artist) => WithField("ARTS", Encoding.UTF8.GetBytes(artist));

    public KfnFileBuilder WithFileKey(byte[] fileKey)
    {
        _fileKey = fileKey;
        return WithField("FLID", fileKey);
    }

    public KfnFileBuilder WithField(string field, byte[] value)
    {
        _fields.Add((field, value));
        return this;
    }

    public KfnFileBuilder WithEntry(string name, int kind, byte[] content, bool encrypt = false)
    {
        _entries.Add((name, kind, content, encrypt));
        return this;
    }

    public KfnFileBuilder WithSongDefinition(string content, bool encrypt = false) =>
        WithEntry("Song.ini", 1, Encoding.UTF8.GetBytes(content), encrypt);

    /// <summary>
    /// Writes a payload verbatim while flagging the entry as encrypted, producing the damaged
    /// containers a real one is never supposed to be: no file key, or a partial cipher block.
    /// </summary>
    public KfnFileBuilder WithEncryptedPayload(string name, int kind, byte[] stored)
    {
        _entries.Add((name, kind, stored, Encrypt: false));
        _malformedEncryption.Add(name);
        return this;
    }

    public string WriteTo(string path)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        file.Write(Encoding.ASCII.GetBytes("KFNB"));

        foreach (var (field, value) in _fields)
        {
            file.Write(Encoding.ASCII.GetBytes(field));
            file.WriteByte(2);
            WriteInt32(file, value.Length);
            file.Write(value);
        }

        file.Write(Encoding.ASCII.GetBytes("ENDH"));
        file.WriteByte(1);
        WriteInt32(file, -1);

        var payloads = _entries.Select(entry => Store(entry.Content, entry.Encrypt)).ToArray();
        WriteInt32(file, _entries.Count);
        var offset = 0;
        for (var index = 0; index < _entries.Count; index++)
        {
            var (name, kind, content, encrypt) = _entries[index];
            var encoded = EncodeWindows1251(name);
            WriteInt32(file, encoded.Length);
            file.Write(encoded);
            WriteInt32(file, kind);
            WriteInt32(file, content.Length);
            WriteInt32(file, offset);
            WriteInt32(file, payloads[index].Length);
            WriteInt32(file, encrypt || _malformedEncryption.Contains(name) ? 1 : 0);
            offset += payloads[index].Length;
        }

        foreach (var payload in payloads) file.Write(payload);
        return path;
    }

    private byte[] Store(byte[] content, bool encrypt)
    {
        if (!encrypt) return content;
        if (_fileKey is null)
        {
            throw new InvalidOperationException("An encrypted entry needs a file key.");
        }

        var padded = new byte[(content.Length + 15) / 16 * 16];
        content.CopyTo(padded, 0);
        using var aes = Aes.Create();
        aes.Key = _fileKey;
        return aes.EncryptEcb(padded, PaddingMode.None);
    }

    /// <summary>Covers ASCII and the Cyrillic block, which is all entry names ever need and
    /// keeps the test project free of System.Text.Encoding.CodePages.</summary>
    public static byte[] EncodeWindows1251(string value)
    {
        var bytes = new byte[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            bytes[index] = character switch
            {
                < (char)0x80 => (byte)character,
                >= 'А' and <= 'я' => (byte)(character - 'А' + 0xC0),
                'ё' => 0xB8,
                'Ё' => 0xA8,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(value),
                    character,
                    "The test encoder covers ASCII and Cyrillic only.")
            };
        }

        return bytes;
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
