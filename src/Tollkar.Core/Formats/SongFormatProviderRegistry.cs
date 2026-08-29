namespace Tollkar.Core.Formats;

public sealed class SongFormatProviderRegistry
{
    private readonly IReadOnlyList<ISongFormatProvider> _providers;

    public SongFormatProviderRegistry(IEnumerable<ISongFormatProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var registeredProviders = providers.ToArray();
        ValidateProviders(registeredProviders);
        _providers = registeredProviders
            .OrderByDescending(provider => provider.Priority)
            .ToArray();
    }

    public IReadOnlyList<ISongFormatProvider> Providers => _providers;

    public ISongFormatProvider? FindProvider(FileCandidate file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _providers.FirstOrDefault(provider => provider.CanHandle(file));
    }

    private static void ValidateProviders(
        IReadOnlyCollection<ISongFormatProvider?> providers)
    {
        if (providers.Any(provider => provider is null))
        {
            throw new ArgumentException(
                "Song format providers cannot contain null elements.",
                nameof(providers));
        }

        foreach (var provider in providers.Cast<ISongFormatProvider>())
        {
            Guard.NotNullOrWhiteSpace(provider.Id, nameof(provider.Id));

            if (provider.Version <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(provider.Version),
                    provider.Version,
                    "Provider version must be greater than zero.");
            }
        }

        var duplicateId = providers
            .Cast<ISongFormatProvider>()
            .GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"A song format provider with ID '{duplicateId}' is already registered.",
                nameof(providers));
        }
    }
}
