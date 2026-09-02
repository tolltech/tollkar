using System.Text;
using Tollkar.Core.Formats.Kfn;
using Tollkar.TestSupport;

namespace Tollkar.Core.Tests.Formats.Kfn;

public sealed class KfnArchiveTests : IDisposable
{
    private static readonly byte[] FileKey =
        [8, 241, 246, 9, 97, 244, 240, 139, 195, 102, 23, 65, 117, 203, 219, 166];

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"tollkar-kfn-tests-{Guid.NewGuid():N}");

    [Fact]
    public void OpenReadsHeaderFieldsAndEntries()
    {
        var path = Build(builder => builder
            .WithTitle("Вера")
            .WithArtist("Кукрыниксы")
            .WithEntry("Вера.mp3", 2, [1, 2, 3])
            .WithSongDefinition("[General]\nTitle=Вера\n"));

        var archive = KfnArchive.Open(path);

        Assert.Equal("Вера", archive.Title);
        Assert.Equal("Кукрыниксы", archive.Artist);
        Assert.Equal(
            [("Вера.mp3", KfnEntryKind.Audio), ("Song.ini", KfnEntryKind.SongDefinition)],
            archive.Entries.Select(entry => (entry.Name, entry.Kind)));
    }

    [Fact]
    public void OpenDecodesWindows1251EntryNames()
    {
        var path = Build(builder => builder
            .WithEntry("Кукрыниксы - Дорогая.avi", 5, [1])
            .WithSongDefinition("[General]\n"));

        var archive = KfnArchive.Open(path);

        Assert.NotNull(archive.FindEntry("Кукрыниксы - Дорогая.avi", KfnEntryKind.Video));
    }

    [Fact]
    public void OpenIgnoresPlaceholderTitles()
    {
        var path = Build(builder => builder
            .WithTitle("-")
            .WithSongDefinition("[General]\n"));

        Assert.Null(KfnArchive.Open(path).Title);
    }

    [Fact]
    public void OpenEntryReturnsOnlyTheEntryWindow()
    {
        var path = Build(builder => builder
            .WithEntry("first.mp3", 2, [1, 2, 3])
            .WithEntry("second.mp3", 2, [4, 5])
            .WithSongDefinition("[General]\n"));
        var archive = KfnArchive.Open(path);

        using var stream = archive.OpenEntry(archive.Entries[1]);
        var content = new byte[8];
        var read = stream.Read(content);

        Assert.Equal(2, stream.Length);
        Assert.Equal(2, read);
        Assert.Equal([4, 5], content[..read]);
    }

    [Fact]
    public void OpenEntrySeeksInsideTheWindow()
    {
        var path = Build(builder => builder
            .WithEntry("first.mp3", 2, [1, 2, 3])
            .WithEntry("second.mp3", 2, [4, 5, 6, 7])
            .WithSongDefinition("[General]\n"));
        var archive = KfnArchive.Open(path);

        using var stream = archive.OpenEntry(archive.Entries[1]);
        stream.Position = 2;
        var content = new byte[4];
        var read = stream.Read(content);

        Assert.Equal(2, read);
        Assert.Equal([6, 7], content[..read]);
    }

    [Fact]
    public void ReadEntryDecryptsUsingTheFileKey()
    {
        var definition = "[General]\nTitle=Акварели\n";
        var path = Build(builder => builder
            .WithFileKey(FileKey)
            .WithSongDefinition(definition, encrypt: true));
        var archive = KfnArchive.Open(path);
        var entry = archive.Entries[0];

        Assert.True(entry.Encrypted);
        Assert.True(entry.StoredLength > entry.Length);
        Assert.Equal(definition, Encoding.UTF8.GetString(archive.ReadEntry(entry)));
    }

    [Fact]
    public void OpenEntryStopsAtTheEndOfTheWindow()
    {
        var path = Build(builder => builder
            .WithEntry("first.mp3", 2, [1, 2, 3])
            .WithEntry("second.mp3", 2, [4, 5])
            .WithSongDefinition("[General]\n"));
        var archive = KfnArchive.Open(path);

        using var stream = archive.OpenEntry(archive.Entries[1]);
        stream.Seek(-1, SeekOrigin.End);
        var content = new byte[4];

        Assert.Equal(1, stream.Read(content));
        Assert.Equal(0, stream.Read(content));
        Assert.Equal(0, stream.Seek(100, SeekOrigin.Begin) - 100);
        Assert.Equal(0, stream.Read(content));
    }

    [Fact]
    public void OpenRejectsEntriesThatRunPastTheEndOfTheFile()
    {
        // A half-copied container: the table still promises payloads the file no longer holds.
        var path = Build(builder => builder
            .WithEntry("song.mp3", 2, [1, 2, 3, 4, 5])
            .WithSongDefinition("[General]\n"));
        var content = File.ReadAllBytes(path);
        File.WriteAllBytes(path, content[..^4]);

        var exception = Assert.Throws<InvalidDataException>(() => KfnArchive.Open(path));
        Assert.Contains("Song.ini", exception.Message);
    }

    [Fact]
    public void ReadEntryRejectsEncryptedEntriesWithoutAFileKey()
    {
        var path = Build(builder => builder
            .WithEncryptedPayload("Song.ini", 1, new byte[16]));
        var archive = KfnArchive.Open(path);

        Assert.Throws<InvalidDataException>(() => archive.ReadEntry(archive.Entries[0]));
    }

    [Fact]
    public void ReadEntryRejectsEncryptedEntriesCutMidBlock()
    {
        var path = Build(builder => builder
            .WithFileKey(FileKey)
            .WithEncryptedPayload("Song.ini", 1, new byte[20]));
        var archive = KfnArchive.Open(path);

        Assert.Throws<InvalidDataException>(() => archive.ReadEntry(archive.Entries[0]));
    }

    [Fact]
    public void OpenRejectsForeignFiles()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "not-a-song.kfn");
        File.WriteAllText(path, "just text");

        Assert.Throws<InvalidDataException>(() => KfnArchive.Open(path));
    }

    [Fact]
    public void OpenRejectsTruncatedContainers()
    {
        var path = Build(builder => builder
            .WithTitle("Вера")
            .WithSongDefinition("[General]\n"));
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..10]);

        Assert.Throws<InvalidDataException>(() => KfnArchive.Open(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string Build(Func<KfnFileBuilder, KfnFileBuilder> configure)
    {
        Directory.CreateDirectory(_directory);
        return configure(new KfnFileBuilder())
            .WriteTo(Path.Combine(_directory, "song.kfn"));
    }
}
