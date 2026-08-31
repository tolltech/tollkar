using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tollkar.Application.Library;
using Tollkar.Infrastructure;
using Tollkar.Web.Catalog;
using Vostok.Logging.Abstractions;

namespace Tollkar.Web.Tests;

public sealed class LibrarySyncTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tollkar-sync-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider clock = new();
    private ILibraryService library = null!;
    private LibrarySyncService worker = null!;

    private string SongsPath => Path.Combine(directory, "songs");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        library = TollkarInfrastructure.CreateLibraryService(Path.Combine(directory, "library.db"));
        await library.InitializeAsync();
        worker = new LibrarySyncService(library, new TestEnvironment { ContentRootPath = directory },
            Options.Create(new LibrarySyncOptions()), clock, new SilentLog());
    }

    public async Task DisposeAsync()
    {
        await worker.StopAsync(CancellationToken.None);
        worker.Dispose();
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task SynchronizesNestedSongsChangesAndDeletionsWithoutDuplicates()
    {
        var nested = Directory.CreateDirectory(Path.Combine(SongsPath, "Album")).FullName;
        var songPath = Path.Combine(nested, "Artist - First.mp4");
        await File.WriteAllBytesAsync(songPath, [1]);
        await File.WriteAllTextAsync(Path.Combine(SongsPath, "ignored.txt"), "not a song");

        await worker.StartAsync(CancellationToken.None);
        var timer = await clock.NextDelayAsync();
        var first = Assert.Single(await library.SearchSongsAsync(new()));
        Assert.Equal("First", first.Title);
        Assert.Equal("Artist", first.Artist);

        timer.Fire();
        timer = await clock.NextDelayAsync();
        Assert.Equal(first, Assert.Single(await library.SearchSongsAsync(new())));
        Assert.Single(await library.GetRootsAsync());

        await File.WriteAllBytesAsync(songPath, [1, 2]);
        await File.WriteAllBytesAsync(Path.Combine(SongsPath, "Artist - Second.mp4"), [3]);
        timer.Fire();
        timer = await clock.NextDelayAsync();
        var songs = await library.SearchSongsAsync(new());
        Assert.Equal(2, songs.Count);
        Assert.Contains(songs, song => song.Id == first.Id);
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "library.db")};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Size FROM Files WHERE Path=$path";
            command.Parameters.AddWithValue("$path", songPath);
            Assert.Equal(2L, await command.ExecuteScalarAsync());
        }

        File.Delete(songPath);
        timer.Fire();
        await clock.NextDelayAsync();
        Assert.Equal("Second", Assert.Single(await library.SearchSongsAsync(new())).Title);
    }

    [Fact]
    public async Task CreatesSongsDirectoryAndStopsWhileWaiting()
    {
        await worker.StartAsync(CancellationToken.None);
        await clock.NextDelayAsync();
        Assert.True(Directory.Exists(SongsPath));
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RetriesAfterDirectoryFailure()
    {
        await File.WriteAllTextAsync(SongsPath, "blocks directory creation");
        await worker.StartAsync(CancellationToken.None);
        var timer = await clock.NextDelayAsync();
        Assert.Empty(await library.GetRootsAsync());

        File.Delete(SongsPath);
        Directory.CreateDirectory(SongsPath);
        await File.WriteAllBytesAsync(Path.Combine(SongsPath, "Recovered.mp4"), [1]);
        timer.Fire();
        await clock.NextDelayAsync();
        Assert.Equal("Recovered", Assert.Single(await library.SearchSongsAsync(new())).Title);
    }

    // Delay registration happens after a complete scan, so tests advance each pass without polling or sleeping.
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly Channel<ManualTimer> delays = Channel.CreateUnbounded<ManualTimer>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            delays.Writer.TryWrite(timer);
            return timer;
        }

        public Task<ManualTimer> NextDelayAsync() =>
            delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Tollkar.Web.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
