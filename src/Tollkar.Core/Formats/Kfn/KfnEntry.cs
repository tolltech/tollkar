namespace Tollkar.Core.Formats.Kfn;

/// <param name="Length">Size of the entry once decrypted.</param>
/// <param name="StoredLength">Size occupied inside the container; larger than
/// <paramref name="Length"/> for encrypted entries because of block padding.</param>
public sealed record KfnEntry(
    string Name,
    KfnEntryKind Kind,
    int Length,
    long Offset,
    int StoredLength,
    bool Encrypted);
