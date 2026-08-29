namespace Tollkar.Core.Playback;

public sealed class SongPlaybackProviderRegistry
{
    private readonly IReadOnlyDictionary<string, ISongPlaybackProvider> _providers;

    public SongPlaybackProviderRegistry(IEnumerable<ISongPlaybackProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var registered = providers.ToArray();
        if (registered.Any(provider => provider is null))
        {
            throw new ArgumentException("Playback providers cannot contain null elements.", nameof(providers));
        }

        _providers = registered.ToDictionary(
            provider => Guard.NotNullOrWhiteSpace(provider.FormatProviderId, nameof(provider.FormatProviderId)),
            StringComparer.OrdinalIgnoreCase);
    }

    public ISongPlaybackProvider? FindProvider(string formatProviderId)
    {
        Guard.NotNullOrWhiteSpace(formatProviderId, nameof(formatProviderId));
        return _providers.GetValueOrDefault(formatProviderId);
    }
}
