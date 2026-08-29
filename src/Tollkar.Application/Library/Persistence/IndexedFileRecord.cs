namespace Tollkar.Application.Library.Persistence;

internal sealed record IndexedFileRecord(
    string Path,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string ProviderId,
    int ProviderVersion);
