using Tollkar.Core.Songs;

namespace Tollkar.Core.Tests.Songs;

public sealed class SongFolderTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "songs");

    [Fact]
    public void FirstFolderUnderTheRootLabelsTheSong()
    {
        Assert.Equal("Сборник", SongFolder.FromPath(Root, Path.Combine(Root, "Сборник", "Кино - Звезда.mp4")));
        Assert.Equal("Сборник", SongFolder.FromPath(Root, Path.Combine(Root, "Сборник", "Диск 2", "Кино - Звезда.mp4")));
        Assert.Equal("Сборник", SongFolder.FromPath(Root + Path.DirectorySeparatorChar, Path.Combine(Root, "Сборник", "Кино - Звезда.mp4")));
    }

    [Fact]
    public void FilesInTheRootOrOutsideItHaveNoFolder()
    {
        Assert.Null(SongFolder.FromPath(Root, Path.Combine(Root, "Кино - Звезда.mp4")));
        Assert.Null(SongFolder.FromPath(Root, Path.Combine(Path.GetTempPath(), "other", "Кино - Звезда.mp4")));
        Assert.Null(SongFolder.FromPath(Root, Path.Combine(Path.GetTempPath(), "songsx", "Кино - Звезда.mp4")));
    }
}
