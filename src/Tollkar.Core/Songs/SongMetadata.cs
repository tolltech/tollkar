namespace Tollkar.Core.Songs;

public sealed record SongMetadata
{
    public SongMetadata(
        string title,
        string? artist,
        TimeSpan? duration,
        SongCapabilities capabilities)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "Duration cannot be negative.");
        }

        Title = Guard.NotNullOrWhiteSpace(title, nameof(title));
        Artist = Guard.NullOrNotWhiteSpace(artist, nameof(artist));
        Duration = duration;
        Capabilities = capabilities;
    }

    public string Title { get; }

    public string? Artist { get; }

    public TimeSpan? Duration { get; }

    public SongCapabilities Capabilities { get; }
}
