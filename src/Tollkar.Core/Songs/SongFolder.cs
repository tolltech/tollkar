namespace Tollkar.Core.Songs;

public static class SongFolder
{
    private static readonly char[] Separators =
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    // Only the first folder under the library root labels a song; files kept in the root have none,
    // and a file outside the root ("..", or another Windows drive) cannot be attributed to any folder.
    public static string? FromPath(string rootPath, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var relative = Path.GetRelativePath(Path.GetFullPath(rootPath), Path.GetFullPath(filePath));
        if (Path.IsPathRooted(relative)) return null;

        var separator = relative.IndexOfAny(Separators);
        if (separator <= 0) return null;

        var folder = relative[..separator];
        return folder is "." or ".." ? null : folder;
    }
}
