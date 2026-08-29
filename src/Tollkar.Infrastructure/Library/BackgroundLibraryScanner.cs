using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Tollkar.Application.Library.Indexing;
using Tollkar.Application.Library.Persistence;
using Tollkar.Core.Formats;
using Tollkar.Core.Songs;

namespace Tollkar.Infrastructure.Library;

internal sealed class BackgroundLibraryScanner(
    ILibraryRepository repository,
    SongFormatProviderRegistry providers,
    int? workerCount = null) : ILibraryScanner
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ScanLocks = new();

    private readonly ILibraryRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly SongFormatProviderRegistry _providers =
        providers ?? throw new ArgumentNullException(nameof(providers));
    private readonly int _workerCount = Math.Clamp(
        workerCount ?? Environment.ProcessorCount / 2,
        1,
        8);

    public async IAsyncEnumerable<LibraryIndexProgress> RefreshAsync(
        Guid rootId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var scanLock = ScanLocks.GetOrAdd(rootId, _ => new SemaphoreSlim(1, 1));
        await scanLock.WaitAsync(cancellationToken);
        try
        {
            using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var progress = Channel.CreateBounded<LibraryIndexProgress>(
                new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });
            var scan = RunScanAsync(rootId, progress.Writer, scanCancellation.Token);

            try
            {
                await foreach (var update in progress.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return update;
                }

                await scan;
            }
            finally
            {
                await scanCancellation.CancelAsync();
                await scan;
            }
        }
        finally
        {
            scanLock.Release();
        }
    }

    private async Task RunScanAsync(
        Guid rootId,
        ChannelWriter<LibraryIndexProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = await _repository.GetRootAsync(rootId, cancellationToken)
                ?? throw new KeyNotFoundException($"Library root '{rootId}' was not found.");
            var scanId = Guid.NewGuid();
            var work = Channel.CreateBounded<ScanWorkItem>(_workerCount * 4);
            var results = Channel.CreateBounded<ScanResult>(_workerCount * 2);
            var discovered = 0;
            var enumerationComplete = true;

            var producer = Task.Run(async () =>
            {
                try
                {
                    var pendingDirectories = new Stack<string>();
                    pendingDirectories.Push(root.Path);
                    while (pendingDirectories.TryPop(out var directoryPath))
                    {
                        IEnumerator<string>? entries = null;
                        try
                        {
                            entries = Directory
                                .EnumerateFileSystemEntries(directoryPath)
                                .GetEnumerator();
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            enumerationComplete = false;
                            await results.Writer.WriteAsync(
                                ScanResult.Failure,
                                cancellationToken);
                            continue;
                        }

                        using (entries)
                        {
                            while (true)
                            {
                                string path;
                                try
                                {
                                    if (!entries.MoveNext()) break;
                                    path = entries.Current;
                                }
                                catch (Exception exception) when (exception is not OperationCanceledException)
                                {
                                    enumerationComplete = false;
                                    await results.Writer.WriteAsync(
                                        ScanResult.Failure,
                                        cancellationToken);
                                    break;
                                }

                                cancellationToken.ThrowIfCancellationRequested();
                                try
                                {
                                    var attributes = File.GetAttributes(path);
                                    if ((attributes & FileAttributes.Directory) != 0)
                                    {
                                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                                        {
                                            pendingDirectories.Push(path);
                                        }
                                        continue;
                                    }

                                    Interlocked.Increment(ref discovered);
                                    var info = new FileInfo(path);
                                    var candidate = new FileCandidate(
                                        info.FullName,
                                        info.Length,
                                        new DateTimeOffset(info.LastWriteTimeUtc));
                                    var provider = _providers.FindProvider(candidate);
                                    if (provider is null)
                                    {
                                        await results.Writer.WriteAsync(
                                            ScanResult.Ignored,
                                            cancellationToken);
                                        continue;
                                    }
                                    await work.Writer.WriteAsync(
                                        new ScanWorkItem(candidate, provider),
                                        cancellationToken);
                                }
                                catch (Exception exception) when (exception is not OperationCanceledException)
                                {
                                    enumerationComplete = false;
                                    await results.Writer.WriteAsync(
                                        ScanResult.ForFailure(path),
                                        cancellationToken);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    work.Writer.TryComplete();
                }
            }, cancellationToken);

            var workers = Enumerable.Range(0, _workerCount)
                .Select(_ => ProcessFilesAsync(rootId, work.Reader, results.Writer, cancellationToken))
                .ToArray();
            var completeResults = Task.Run(async () =>
            {
                try
                {
                    await producer;
                    await Task.WhenAll(workers);
                    results.Writer.TryComplete();
                }
                catch (Exception exception)
                {
                    results.Writer.TryComplete(exception);
                }
            }, CancellationToken.None);

            var indexed = 0;
            var unchanged = 0;
            var ignored = 0;
            var failed = 0;
            await foreach (var result in results.Reader.ReadAllAsync(cancellationToken))
            {
                if (result.Kind == ScanResultKind.Indexed)
                {
                    try
                    {
                        await _repository.UpsertSongAsync(
                            rootId,
                            result.Item!.File,
                            result.Item.Provider.Id,
                            result.Item.Provider.Version,
                            result.Metadata!,
                            cancellationToken);
                        await _repository.MarkFileSeenAsync(
                            result.Item.File.Path,
                            scanId,
                            cancellationToken);
                        indexed++;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        enumerationComplete = false;
                        failed++;
                    }
                }
                else if (result.Kind == ScanResultKind.Unchanged)
                {
                    await _repository.MarkFileSeenAsync(
                        result.Item!.File.Path,
                        scanId,
                        cancellationToken);
                    unchanged++;
                }
                else if (result.Kind == ScanResultKind.Ignored) ignored++;
                else
                {
                    if (result.Item is not null)
                    {
                        try
                        {
                            await _repository.MarkFileSeenAsync(
                                result.Item.File.Path,
                                scanId,
                                cancellationToken);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            enumerationComplete = false;
                        }
                    }
                    else if (result.Path is not null)
                    {
                        await _repository.MarkFileSeenAsync(
                            result.Path,
                            scanId,
                            cancellationToken);
                    }

                    failed++;
                }

                progress.TryWrite(CreateProgress(
                    rootId, discovered, indexed, unchanged, ignored, failed, false));
            }

            await completeResults;
            if (enumerationComplete)
            {
                await _repository.RemoveFilesNotSeenAsync(
                    rootId,
                    scanId,
                    cancellationToken);
            }
            progress.TryWrite(CreateProgress(
                rootId, discovered, indexed, unchanged, ignored, failed, true));
            progress.TryComplete();
        }
        catch (Exception exception)
        {
            progress.TryComplete(exception);
        }
    }

    private async Task ProcessFilesAsync(
        Guid rootId,
        ChannelReader<ScanWorkItem> work,
        ChannelWriter<ScanResult> results,
        CancellationToken cancellationToken)
    {
        await foreach (var item in work.ReadAllAsync(cancellationToken))
        {
            try
            {
                var existing = await _repository.GetIndexedFileAsync(
                    item.File.Path,
                    cancellationToken);
                if (IsUnchanged(existing, item))
                {
                    await results.WriteAsync(
                        ScanResult.ForUnchanged(item),
                        cancellationToken);
                    continue;
                }

                var metadata = await item.Provider.ReadMetadataAsync(
                    item.File,
                    cancellationToken);
                await results.WriteAsync(
                    ScanResult.ForIndex(item, metadata),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await results.WriteAsync(
                    ScanResult.ForFailure(item),
                    cancellationToken);
            }
        }
    }

    private static bool IsUnchanged(IndexedFileRecord? existing, ScanWorkItem item) =>
        existing is not null &&
        existing.Size == item.File.Size &&
        existing.LastWriteTimeUtc == item.File.LastWriteTimeUtc &&
        existing.ProviderId == item.Provider.Id &&
        existing.ProviderVersion == item.Provider.Version;

    private static LibraryIndexProgress CreateProgress(
        Guid rootId,
        int discovered,
        int indexed,
        int unchanged,
        int ignored,
        int failed,
        bool completed) =>
        new(rootId, discovered, indexed, failed, completed)
        {
            UnchangedFiles = unchanged,
            IgnoredFiles = ignored
        };

    private sealed record ScanWorkItem(
        FileCandidate File,
        ISongFormatProvider Provider);

    private enum ScanResultKind { Indexed, Unchanged, Ignored, Failure }

    private sealed record ScanResult(
        ScanResultKind Kind,
        ScanWorkItem? Item = null,
        SongMetadata? Metadata = null,
        string? Path = null)
    {
        public static ScanResult Ignored { get; } = new(ScanResultKind.Ignored);
        public static ScanResult Failure { get; } = new(ScanResultKind.Failure);

        public static ScanResult ForIndex(
            ScanWorkItem item,
            SongMetadata metadata) =>
            new(ScanResultKind.Indexed, item, metadata);

        public static ScanResult ForUnchanged(ScanWorkItem item) =>
            new(ScanResultKind.Unchanged, item);

        public static ScanResult ForFailure(ScanWorkItem item) =>
            new(ScanResultKind.Failure, item);

        public static ScanResult ForFailure(string path) =>
            new(ScanResultKind.Failure, Path: path);
    }
}
