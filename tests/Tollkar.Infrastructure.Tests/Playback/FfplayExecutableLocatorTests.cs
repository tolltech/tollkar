using Tollkar.Infrastructure.Playback;

namespace Tollkar.Infrastructure.Tests.Playback;

public sealed class FfplayExecutableLocatorTests
{
    [Fact]
    public void FindsExecutableInPath()
    {
        var separator = Path.PathSeparator;
        var path = $"/first{separator}/video-tools{separator}/last";

        var result = FfplayExecutableLocator.Find(
            path,
            candidate => candidate == "/video-tools/ffplay");

        Assert.Equal("/video-tools/ffplay", result);
    }

    [Fact]
    public void FallsBackToHomebrewLocation()
    {
        var result = FfplayExecutableLocator.Find(
            string.Empty,
            candidate => candidate == "/opt/homebrew/bin/ffplay");

        Assert.Equal("/opt/homebrew/bin/ffplay", result);
    }

    [Fact]
    public void ReturnsNullWhenExecutableIsMissing()
    {
        var result = FfplayExecutableLocator.Find("/first:/second", _ => false);

        Assert.Null(result);
    }
}
