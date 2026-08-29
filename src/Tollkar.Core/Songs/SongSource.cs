namespace Tollkar.Core.Songs;

public sealed record SongSource
{
    public SongSource(string providerId, string filePath, string? internalId = null)
    {
        ProviderId = Guard.NotNullOrWhiteSpace(providerId, nameof(providerId));
        FilePath = Guard.NotNullOrWhiteSpace(filePath, nameof(filePath));
        InternalId = Guard.NullOrNotWhiteSpace(internalId, nameof(internalId));
    }

    public string ProviderId { get; }

    public string FilePath { get; }

    public string? InternalId { get; }
}
