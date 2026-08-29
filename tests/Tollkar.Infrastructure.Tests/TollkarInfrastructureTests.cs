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

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
