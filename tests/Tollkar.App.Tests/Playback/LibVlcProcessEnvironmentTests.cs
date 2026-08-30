using Tollkar.App.Playback;

namespace Tollkar.App.Tests.Playback;

public sealed class LibVlcProcessEnvironmentTests
{
    [Fact]
    public void CreateStartInfoPreservesDotnetEntryAssemblyAndArguments()
    {
        var runtime = new LibVlcRuntime("/vlc/lib", "/vlc/plugins");

        var startInfo = LibVlcProcessEnvironment.CreateStartInfo(
            "/usr/local/bin/dotnet",
            "/app/Tollkar.App.dll",
            ["--library", "/music with spaces"],
            runtime);

        Assert.Equal("/usr/local/bin/dotnet", startInfo.FileName);
        Assert.Equal(
            ["/app/Tollkar.App.dll", "--library", "/music with spaces"],
            startInfo.ArgumentList);
        Assert.Equal(runtime.LibraryDirectory, startInfo.Environment["DYLD_LIBRARY_PATH"]);
        Assert.Equal(runtime.PluginsDirectory, startInfo.Environment["VLC_PLUGIN_PATH"]);
        Assert.Equal("1", startInfo.Environment[LibVlcProcessEnvironment.RestartMarker]);
    }

    [Fact]
    public void CreateStartInfoDoesNotAddEntryAssemblyForAppHost()
    {
        var startInfo = LibVlcProcessEnvironment.CreateStartInfo(
            "/Applications/Tollkar.app/Contents/MacOS/Tollkar",
            "/app/Tollkar.App.dll",
            ["--library", "/music"],
            new LibVlcRuntime("/vlc/lib", "/vlc/plugins"));

        Assert.Equal(["--library", "/music"], startInfo.ArgumentList);
    }
}
