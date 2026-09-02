using System.Text;
using Tollkar.Core.Formats.Kfn;
using Tollkar.TestSupport;

namespace Tollkar.Core.Tests.Formats.Kfn;

public sealed class KfnSongDefinitionTests
{
    [Fact]
    public void ParseReadsGeneralSection()
    {
        var definition = Parse("""
            [General]
            Title=Вера
            Artist=Кукрыниксы
            Source=1,I,Вера.mp3
            """);

        Assert.Equal("Вера", definition.Title);
        Assert.Equal("Кукрыниксы", definition.Artist);
        Assert.Equal("Вера.mp3", definition.AudioFileName);
    }

    [Fact]
    public void ParseTakesBackgroundFromTheEffectThatDeclaresIt()
    {
        var definition = Parse("""
            [General]
            Title=Дорогая
            [Eff1]
            ID=62
            VideoFile=Кукрыниксы - Дорогая.avi
            LoopVideo=1
            [Eff2]
            ID=2
            TextCount=1
            Text0=ВЕ/ЧЕР
            Sync0=100,130
            """);

        Assert.Equal("Кукрыниксы - Дорогая.avi", definition.BackgroundFileName);
        Assert.True(definition.LoopBackground);
    }

    [Fact]
    public void ParseIgnoresTheGeneratedVisualizationBackground()
    {
        var definition = Parse("""
            [Eff1]
            VideoFile=UseMusicSource
            LoopVideo=0
            """);

        Assert.Null(definition.BackgroundFileName);
    }

    [Fact]
    public void ParseSplitsLinesIntoSyllablesKeepingWordSpacing()
    {
        var definition = Parse("""
            [Eff1]
            TextCount=1
            Text0=ЧТО ВСЁ ТАК БЛИЗ/КО
            Sync0=2114,2144,2232,2266,2306
            """);

        var line = Assert.Single(definition.Lines);
        Assert.Equal(
            ["ЧТО ", "ВСЁ ", "ТАК ", "БЛИЗ", "КО"],
            line.Syllables.Select(syllable => syllable.Text));
        Assert.Equal("ЧТО ВСЁ ТАК БЛИЗ/КО".Replace("/", string.Empty),
            string.Concat(line.Syllables.Select(syllable => syllable.Text)));
    }

    [Fact]
    public void ParseConvertsSyncMarksToMillisecondsAndEndsEachSyllableAtTheNext()
    {
        var definition = Parse("""
            [Eff1]
            TextCount=1
            Text0=КА/ЖЕТ/СЯ
            Sync0=2032,2062,2086
            """);

        var line = Assert.Single(definition.Lines);
        Assert.Equal(20320, line.StartMs);
        Assert.Equal([20320, 20620, 20860], line.Syllables.Select(syllable => syllable.StartMs));
        Assert.Equal([20620, 20860, 21860], line.Syllables.Select(syllable => syllable.EndMs));
    }

    [Fact]
    public void ParseCapsSyllablesThatAreFollowedByAnInstrumentalBreak()
    {
        var definition = Parse("""
            [Eff1]
            TextCount=1
            Text0=КО/НЕЦ
            Sync0=100,130,5000
            """);

        var line = Assert.Single(definition.Lines);
        Assert.Equal(2300, line.Syllables[^1].EndMs);
        Assert.Equal(2300, line.EndMs);
    }

    [Fact]
    public void ParseJoinsSyncMarksSplitAcrossKeys()
    {
        var definition = Parse("""
            [Eff1]
            TextCount=2
            Text0=РАЗ/ДВА
            Text1=ТРИ
            Sync0=100,130
            Sync1=200
            """);

        Assert.Equal([100 * 10, 200 * 10], definition.Lines.Select(line => line.StartMs));
    }

    [Fact]
    public void ParseSkipsBlankLayoutLinesWithoutConsumingSyncMarks()
    {
        var definition = Parse("""
            [Eff1]
            TextCount=3
            Text0=РАЗ
            Text1=
            Text2=ДВА
            Sync0=100,200
            """);

        Assert.Equal([1000, 2000], definition.Lines.Select(line => line.StartMs));
    }

    [Fact]
    public void ParseKeepsTimedLinesWhenSyncMarksRunOut()
    {
        var definition = Parse("""
            [Eff1]
            TextCount=2
            Text0=РАЗ/ДВА
            Text1=ТРИ
            Sync0=100,130
            """);

        var line = Assert.Single(definition.Lines);
        Assert.Equal(2, line.Syllables.Count);
    }

    [Fact]
    public void ParseIgnoresSurplusSyncMarks()
    {
        var definition = Parse("""
            [Eff1]
            TextCount=1
            Text0=РАЗ
            Sync0=100,130,160
            """);

        var line = Assert.Single(definition.Lines);
        Assert.Equal(1000, line.StartMs);
    }

    [Fact]
    public void ParseAppliesTheGlobalShift()
    {
        var definition = Parse("""
            [General]
            GlobalShift=50
            [Eff1]
            TextCount=1
            Text0=РАЗ
            Sync0=100
            """);

        Assert.Equal(1500, definition.Lines[0].StartMs);
    }

    [Fact]
    public void ParseAcceptsWindows1251Values()
    {
        var content = KfnFileBuilder.EncodeWindows1251("[General]\nSource=1,I,Вера.mp3\n");

        Assert.Equal("Вера.mp3", KfnSongDefinition.Parse(content).AudioFileName);
    }

    [Fact]
    public void ParseAcceptsWindowsLineEndings()
    {
        var definition = Parse("[General]\r\nTitle=Вера\r\n");

        Assert.Equal("Вера", definition.Title);
    }

    private static KfnSongDefinition Parse(string content) =>
        KfnSongDefinition.Parse(Encoding.UTF8.GetBytes(content));
}
