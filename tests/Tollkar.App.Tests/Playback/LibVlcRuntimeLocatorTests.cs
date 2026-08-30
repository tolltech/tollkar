using Tollkar.App.Playback;

namespace Tollkar.App.Tests.Playback;

public sealed class LibVlcRuntimeLocatorTests
{
    [Fact]
    public void CreateRuntimeFindsVlcApplicationLayout()
    {
        using var directory = TemporaryDirectory.Create();
        var libraryDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "lib"));
        var pluginsDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "plugins"));
        File.WriteAllText(Path.Combine(libraryDirectory.FullName, "libvlc.dylib"), string.Empty);
        File.WriteAllText(Path.Combine(libraryDirectory.FullName, "libvlccore.dylib"), string.Empty);

        var runtime = LibVlcRuntimeLocator.CreateRuntime(directory.Path);

        Assert.Equal(libraryDirectory.FullName, runtime?.LibraryDirectory);
        Assert.Equal(pluginsDirectory.FullName, runtime?.PluginsDirectory);
    }

    [Fact]
    public void CreateRuntimeFindsPackagedLayout()
    {
        using var directory = TemporaryDirectory.Create();
        var pluginsDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "plugins"));
        File.WriteAllText(Path.Combine(directory.Path, "libvlc.dylib"), string.Empty);
        File.WriteAllText(Path.Combine(directory.Path, "libvlccore.dylib"), string.Empty);

        var runtime = LibVlcRuntimeLocator.CreateRuntime(directory.Path);

        Assert.Equal(directory.Path, runtime?.LibraryDirectory);
        Assert.Equal(pluginsDirectory.FullName, runtime?.PluginsDirectory);
    }

    [Fact]
    public void CreateRuntimeRejectsIncompleteInstallation()
    {
        using var directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, "plugins"));

        Assert.Null(LibVlcRuntimeLocator.CreateRuntime(directory.Path));
    }

    [Fact]
    public void FindAllPrefersPackagedAndApplicationRuntimes()
    {
        using var configured = TemporaryDirectory.Create();
        using var application = TemporaryDirectory.Create();
        using var packaged = TemporaryDirectory.Create();
        CreatePackagedRuntime(configured.Path);
        CreatePackagedRuntime(application.Path);
        CreatePackagedRuntime(packaged.Path);

        var runtimes = LibVlcRuntimeLocator.FindAll(
            configured.Path,
            application.Path,
            packaged.Path);

        Assert.Equal(3, runtimes.Count);
        Assert.Equal(packaged.Path, runtimes[0].LibraryDirectory);
        Assert.Equal(application.Path, runtimes[1].LibraryDirectory);
        Assert.Equal(configured.Path, runtimes[2].LibraryDirectory);
    }

    private static void CreatePackagedRuntime(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "plugins"));
        File.WriteAllText(Path.Combine(path, "libvlc.dylib"), string.Empty);
        File.WriteAllText(Path.Combine(path, "libvlccore.dylib"), string.Empty);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create() =>
            new(Directory.CreateTempSubdirectory("tollkar-libvlc-").FullName);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
