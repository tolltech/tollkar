using Tollkar.Core.Formats;
using Tollkar.Core.Songs;

namespace Tollkar.Core.Tests.Formats;

public sealed class SongFormatProviderRegistryTests
{
    private static readonly FileCandidate Candidate = new(
        "/music/Artist - Song.mp4",
        size: 1024,
        lastWriteTime: DateTimeOffset.UnixEpoch);

    [Fact]
    public void FindProviderReturnsHighestPriorityMatchingProvider()
    {
        var lowerPriority = new StubProvider("first", priority: 10, canHandle: true);
        var higherPriority = new StubProvider("second", priority: 20, canHandle: true);
        var registry = new SongFormatProviderRegistry([lowerPriority, higherPriority]);

        var provider = registry.FindProvider(Candidate);

        Assert.Same(higherPriority, provider);
    }

    [Fact]
    public void FindProviderReturnsNullWhenFormatIsUnknown()
    {
        var registry = new SongFormatProviderRegistry(
            [new StubProvider("known", canHandle: false)]);

        var provider = registry.FindProvider(Candidate);

        Assert.Null(provider);
    }

    [Fact]
    public void ConstructorRejectsDuplicateProviderIdsIgnoringCase()
    {
        var providers = new ISongFormatProvider[]
        {
            new StubProvider("video", canHandle: true),
            new StubProvider("VIDEO", canHandle: false)
        };

        var exception = Assert.Throws<ArgumentException>(
            () => new SongFormatProviderRegistry(providers));

        Assert.Contains("video", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsInvalidProviderId(string? id)
    {
        var provider = new StubProvider(id!, canHandle: false);

        Assert.Throws<ArgumentException>(
            () => new SongFormatProviderRegistry([provider]));
    }

    [Fact]
    public void ConstructorRejectsNonPositiveProviderVersion()
    {
        var provider = new StubProvider("video", version: 0, canHandle: false);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SongFormatProviderRegistry([provider]));
    }

    [Fact]
    public void ConstructorRejectsNullProvider()
    {
        var providers = new ISongFormatProvider[] { null! };

        Assert.Throws<ArgumentException>(
            () => new SongFormatProviderRegistry(providers));
    }

    private sealed class StubProvider(
        string id,
        int version = 1,
        int priority = 0,
        bool canHandle = false) : ISongFormatProvider
    {
        public string Id => id;

        public int Version => version;

        public int Priority => priority;

        public bool CanHandle(FileCandidate file) => canHandle;

        public ValueTask<SongMetadata> ReadMetadataAsync(
            FileCandidate file,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
