namespace Tollkar.Infrastructure.Tests;

public sealed class TollkarInfrastructureTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"tollkar-composition-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateLibraryServiceBuildsWorkingServiceGraph()
    {
        var service = TollkarInfrastructure.CreateLibraryService(
            Path.Combine(_directory, "library.db"));

        await service.InitializeAsync();

        Assert.Empty(await service.GetRootsAsync());
    }

    [Fact]
    public async Task PlaybackQueueCanInitializeBeforeLibraryIsUsed()
    {
        var services = TollkarInfrastructure.CreateServices(
            Path.Combine(_directory, "queue-first.db"));

        await services.PlaybackQueue.InitializeAsync();

        Assert.Empty(await services.PlaybackQueue.GetItemsAsync());
    }

    [Fact]
    public async Task PlaybackQueuePersistsAndCanBeReordered()
    {
        var databasePath = Path.Combine(_directory, "queue.db");
        var mediaPath = Path.Combine(_directory, "media");
        Directory.CreateDirectory(mediaPath);
        await File.WriteAllBytesAsync(Path.Combine(mediaPath, "Artist - First.mp4"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(mediaPath, "Artist - Second.mp4"), [2]);
        var services = TollkarInfrastructure.CreateServices(databasePath);
        await services.Library.InitializeAsync();
        var root = await services.Library.AddRootAsync(mediaPath);
        await foreach (var _ in services.Library.RefreshRootAsync(root.Id)) { }
        var songs = await services.Library.SearchSongsAsync(new());

        await services.PlaybackQueue.AddAsync(songs[0].Id);
        await services.PlaybackQueue.AddAsync(songs[1].Id);
        var original = await services.PlaybackQueue.GetItemsAsync();
        await services.PlaybackQueue.MoveByAsync(original[1].Id, -1);
        await services.PlaybackQueue.MoveByAsync(original[1].Id, -1);

        var reopened = TollkarInfrastructure.CreateServices(databasePath);
        await reopened.Library.InitializeAsync();
        var persisted = await reopened.PlaybackQueue.GetItemsAsync();
        Assert.Equal([original[1].SongId, original[0].SongId], persisted.Select(item => item.SongId));

        await reopened.PlaybackQueue.RemoveAsync(persisted[0].Id);
        var remaining = Assert.Single(await reopened.PlaybackQueue.GetItemsAsync());
        Assert.Equal(0, remaining.Position);

        await reopened.PlaybackQueue.AddAsync(songs[1].Id);
        var retained = (await reopened.PlaybackQueue.GetItemsAsync())[1];
        await reopened.PlaybackQueue.RemoveAllExceptAsync(retained.Id);
        var cleared = Assert.Single(await reopened.PlaybackQueue.GetItemsAsync());
        Assert.Equal(retained.Id, cleared.Id);
        Assert.Equal(0, cleared.Position);

        await reopened.PlaybackQueue.RemoveAllExceptAsync(null);
        Assert.Empty(await reopened.PlaybackQueue.GetItemsAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
