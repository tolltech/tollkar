namespace Tollkar.Core.Songs;

public sealed record Song
{
    public Song(Guid id, SongMetadata metadata, SongSource source)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Song ID cannot be empty.", nameof(id));
        }

        Id = id;
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public Guid Id { get; }

    public SongMetadata Metadata { get; }

    public SongSource Source { get; }
}
