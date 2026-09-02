using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Tollkar.Core.Formats.Kfn;

/// <summary>
/// Reads the KaraFun container: a signature, a list of header fields terminated by "ENDH",
/// a table of entries and then their payloads written back to back.
/// </summary>
public sealed class KfnArchive
{
    private const string Signature = "KFNB";
    private const string HeaderTerminator = "ENDH";
    private const string TitleField = "TITL";
    private const string ArtistField = "ARTS";
    private const string FileKeyField = "FLID";
    private const int MaximumFieldLength = 64 * 1024;
    private const int MaximumEntryCount = 4096;
    private const int MaximumBufferedEntryLength = 8 * 1024 * 1024;
    private const int FileKeyLength = 16;
    private const int AesBlockLength = 16;

    private readonly string _path;
    private readonly long _dataOrigin;
    private readonly byte[]? _fileKey;

    private KfnArchive(
        string path,
        long dataOrigin,
        byte[]? fileKey,
        string? title,
        string? artist,
        IReadOnlyList<KfnEntry> entries)
    {
        _path = path;
        _dataOrigin = dataOrigin;
        _fileKey = fileKey;
        Title = title;
        Artist = artist;
        Entries = entries;
    }

    public string? Title { get; }

    public string? Artist { get; }

    public IReadOnlyList<KfnEntry> Entries { get; }

    public static KfnArchive Open(string path)
    {
        Guard.NotNullOrWhiteSpace(path, nameof(path));

        using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);

        try
        {
            return Read(path, file);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException($"'{path}' is a truncated KFN container.", exception);
        }
    }

    public KfnEntry? FindEntry(string name, KfnEntryKind kind) =>
        Entries.FirstOrDefault(entry =>
            entry.Kind == kind &&
            string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

    public KfnEntry? FirstEntry(KfnEntryKind kind) =>
        Entries.FirstOrDefault(entry => entry.Kind == kind);

    /// <summary>
    /// Opens the entry payload. Plain entries stay a seekable window over the container so that
    /// large tracks are never buffered; encrypted entries are small and are decrypted in memory.
    /// </summary>
    public Stream OpenEntry(KfnEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Encrypted)
        {
            return new MemoryStream(ReadEntry(entry), writable: false);
        }

        var file = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        return new KfnEntryStream(file, _dataOrigin + entry.Offset, entry.Length);
    }

    /// <summary>
    /// Reads a whole entry into memory. Meant for the song definition; larger payloads belong in
    /// <see cref="OpenEntry"/>, which streams them.
    /// </summary>
    public byte[] ReadEntry(KfnEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.StoredLength > MaximumBufferedEntryLength)
        {
            throw new InvalidDataException(
                $"Entry '{entry.Name}' is too large to read into memory.");
        }

        using var file = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.RandomAccess);

        var stored = new byte[entry.StoredLength];
        file.Position = _dataOrigin + entry.Offset;
        file.ReadExactly(stored);

        return entry.Encrypted ? Decrypt(stored, entry.Length) : stored[..entry.Length];
    }

    private byte[] Decrypt(byte[] stored, int length)
    {
        if (_fileKey is null)
        {
            throw new InvalidDataException(
                "An encrypted entry requires the FLID header field, which is missing.");
        }

        // Reported as damaged data rather than a cryptographic failure: callers treat an
        // unreadable container as a missing song, and a partial block is exactly that.
        if (stored.Length % AesBlockLength != 0)
        {
            throw new InvalidDataException(
                "An encrypted entry must be a whole number of cipher blocks.");
        }

        using var aes = Aes.Create();
        aes.Key = _fileKey;

        return aes.DecryptEcb(stored, PaddingMode.None)[..length];
    }

    private static KfnArchive Read(string path, FileStream file)
    {
        Span<byte> signature = stackalloc byte[4];
        file.ReadExactly(signature);
        if (KfnText.Decode(signature) != Signature)
        {
            throw new InvalidDataException($"'{path}' is not a KFN container.");
        }

        string? title = null;
        string? artist = null;
        byte[]? fileKey = null;
        string field;
        do
        {
            field = ReadFieldName(file);
            var type = ReadByte(file);
            switch (type)
            {
                case 1:
                    ReadInt32(file);
                    break;
                case 2:
                    var value = ReadBytes(file, ReadLength(file, MaximumFieldLength));
                    switch (field)
                    {
                        case TitleField: title = KfnText.Decode(value); break;
                        case ArtistField: artist = KfnText.Decode(value); break;
                        case FileKeyField when value.Length == FileKeyLength: fileKey = value; break;
                    }

                    break;
                default:
                    throw new InvalidDataException(
                        $"Header field '{field}' has unknown type {type}.");
            }
        } while (field != HeaderTerminator);

        var entries = ReadEntries(file);
        var dataOrigin = file.Position;
        // Offsets are only meaningful once the payload section is located, so containment is
        // checked here rather than while reading the table.
        foreach (var entry in entries)
        {
            if (dataOrigin + entry.Offset + entry.StoredLength > file.Length)
            {
                throw new InvalidDataException($"Entry '{entry.Name}' runs past the end of '{path}'.");
            }
        }

        return new KfnArchive(
            path,
            dataOrigin,
            fileKey,
            KfnText.Meaningful(title),
            KfnText.Meaningful(artist),
            entries);
    }

    private static IReadOnlyList<KfnEntry> ReadEntries(FileStream file)
    {
        var count = ReadLength(file, MaximumEntryCount);
        var entries = new List<KfnEntry>(count);
        for (var index = 0; index < count; index++)
        {
            var name = KfnText.Decode(ReadBytes(file, ReadLength(file, MaximumFieldLength)));
            var kind = ReadInt32(file);
            var length = ReadInt32(file);
            var offset = ReadInt32(file);
            var storedLength = ReadInt32(file);
            var encrypted = ReadInt32(file);

            if (length < 0 || offset < 0 || storedLength < length)
            {
                throw new InvalidDataException($"Entry '{name}' has an invalid layout.");
            }

            entries.Add(new KfnEntry(
                name,
                Enum.IsDefined((KfnEntryKind)kind) ? (KfnEntryKind)kind : KfnEntryKind.Unknown,
                length,
                offset,
                storedLength,
                encrypted != 0));
        }

        return entries;
    }

    private static string ReadFieldName(FileStream file) => KfnText.Decode(ReadBytes(file, 4));

    private static byte ReadByte(FileStream file)
    {
        var value = file.ReadByte();
        if (value < 0) throw new InvalidDataException("The KFN header ended unexpectedly.");
        return (byte)value;
    }

    private static int ReadInt32(FileStream file) =>
        BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(file, 4));

    private static int ReadLength(FileStream file, int maximum)
    {
        var value = ReadInt32(file);
        if (value < 0 || value > maximum)
        {
            throw new InvalidDataException($"The KFN header declares an implausible length {value}.");
        }

        return value;
    }

    private static byte[] ReadBytes(FileStream file, int count)
    {
        var buffer = new byte[count];
        file.ReadExactly(buffer);
        return buffer;
    }
}
