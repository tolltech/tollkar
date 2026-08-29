namespace Tollkar.Core.Formats;

public sealed record FileCandidate
{
    public FileCandidate(string path, long size, DateTimeOffset lastWriteTime)
    {
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                "File size cannot be negative.");
        }

        Path = Guard.NotNullOrWhiteSpace(path, nameof(path));
        Size = size;
        LastWriteTimeUtc = lastWriteTime.ToUniversalTime();
    }

    public string Path { get; }

    public long Size { get; }

    public DateTimeOffset LastWriteTimeUtc { get; }
}
