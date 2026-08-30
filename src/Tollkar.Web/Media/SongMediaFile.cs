namespace Tollkar.Web.Media;

internal static class SongMediaFile
{
    public static FileStream? Open(string root, string filePath)
    {
        try
        {
            if (!Path.IsPathFullyQualified(filePath) ||
                !string.Equals(Path.GetExtension(filePath), ".mp4", StringComparison.OrdinalIgnoreCase))
                return null;

            var path = Path.GetFullPath(filePath);
            var relative = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relative) || relative == "." || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return null;

            // A lexical root check alone allows links to expose files outside the media directory.
            var current = root;
            if (IsLink(current)) return null;
            foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, segment);
                if (IsLink(current)) return null;
            }

            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                        or ArgumentException or NotSupportedException)
        {
            // Stale catalog entries and unavailable files must not disclose server paths.
            return null;
        }
    }

    private static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
