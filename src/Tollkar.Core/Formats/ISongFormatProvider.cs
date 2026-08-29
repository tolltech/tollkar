using Tollkar.Core.Songs;

namespace Tollkar.Core.Formats;

public interface ISongFormatProvider
{
    string Id { get; }

    int Version { get; }

    int Priority { get; }

    bool CanHandle(FileCandidate file);

    ValueTask<SongMetadata> ReadMetadataAsync(
        FileCandidate file,
        CancellationToken cancellationToken = default);
}
