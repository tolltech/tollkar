using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tollkar.Application.Library;
using Tollkar.TestSupport;
using Tollkar.Web.Catalog;
using Tollkar.Web.Media;

namespace Tollkar.Web.Tests;

public sealed class KaraokeMediaTests : IAsyncLifetime
{
    private static readonly byte[] Audio = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
    private static readonly byte[] Mp4Clip = [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 1, 2];
    private static readonly byte[] AviClip = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0];
    private const string Definition = """
        [General]
        Title=Дорогая
        Artist=Кукрыниксы
        Source=1,I,Дорогая.mp3
        [Eff1]
        VideoFile=фон.avi
        LoopVideo=1
        [Eff2]
        TextCount=2
        Text0=ВЕ/ЧЕР ЧЁР/НЫЕ
        Text1=ГЛА/ЗА
        Sync0=100,130,180,220
        Sync1=300,340
        """;

    private readonly AuthApplication application = new();
    private string directory = null!;
    private Guid rootId;
    private Guid songId;

    public async Task InitializeAsync()
    {
        await application.InitializeDatabaseAsync();
        directory = application.Services.GetRequiredService<IOptions<LibrarySyncOptions>>().Value.SongsPath;
        Directory.CreateDirectory(Path.Combine(directory, "КУКРЫНИКСЫ"));
        Build("Дорогая.kfn", Mp4Clip);
        rootId = (await Library.AddRootAsync(directory)).Id;
        songId = await IndexAsync("Дорогая");
    }

    public async Task DisposeAsync() => await application.DisposeAsync();

    [Fact]
    public async Task StreamsTheEmbeddedTrackAsAudio()
    {
        using var client = await LoginAsync();
        using var response = await client.GetAsync($"/api/songs/{songId}/media");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Audio, await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("bytes", response.Headers.AcceptRanges);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task StreamsRequestedRangeOfTheEmbeddedTrack()
    {
        using var client = await LoginAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/songs/{songId}/media");
        request.Headers.Add("Range", "bytes=10-19");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal($"bytes 10-19/{Audio.Length}", response.Content.Headers.ContentRange?.ToString());
        Assert.Equal(Audio[10..20], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task StreamsTheEmbeddedBackground()
    {
        using var client = await LoginAsync();
        using var response = await client.GetAsync($"/api/songs/{songId}/background");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Mp4Clip, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task BackgroundsBrowsersCannotPlayAreNotServed()
    {
        Build("Дорогая.kfn", AviClip);
        using var client = await LoginAsync();

        using var response = await client.GetAsync($"/api/songs/{songId}/background");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null((await Script(client)).Background);
    }

    [Fact]
    public async Task ReadsTheKaraokeScript()
    {
        using var client = await LoginAsync();

        var script = await Script(client);

        Assert.True(script.Background?.Loop);
        Assert.Equal([1000, 3000], script.Lines.Select(line => line.StartMs));
        Assert.Equal(
            ["ВЕ", "ЧЕР ", "ЧЁР", "НЫЕ"],
            script.Lines[0].Syllables.Select(syllable => syllable.Text));
        Assert.Equal([1000, 1300, 1800, 2200], script.Lines[0].Syllables.Select(s => s.StartMs));
    }

    [Fact]
    public async Task VideoSongsHaveNoKaraokeScriptOrBackground()
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, "Кино - Пачка сигарет.mp4"), Audio);
        var videoId = await IndexAsync("Пачка");
        using var client = await LoginAsync();

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/songs/{videoId}/karaoke")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/songs/{videoId}/background")).StatusCode);
    }

    [Theory]
    [InlineData("media")]
    [InlineData("background")]
    [InlineData("karaoke")]
    public async Task AnonymousRequestsCannotReadKaraoke(string resource)
    {
        using var client = application.CreateSession();

        using var response = await client.GetAsync($"/api/songs/{songId}/{resource}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("media")]
    [InlineData("background")]
    [InlineData("karaoke")]
    public async Task DamagedContainersReadAsMissingWithoutExposingPaths(string resource)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "КУКРЫНИКСЫ", "Дорогая.kfn"), "broken");
        using var client = await LoginAsync();

        using var response = await client.GetAsync($"/api/songs/{songId}/{resource}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(directory, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("media")]
    [InlineData("background")]
    [InlineData("karaoke")]
    public async Task UnreadableEncryptionReadsAsMissingRatherThanFailing(string resource)
    {
        // A container whose table parses but whose encrypted script is not whole cipher blocks.
        new KfnFileBuilder()
            .WithEntry("Дорогая.mp3", 2, Audio)
            .WithEncryptedPayload("Song.ini", 1, new byte[20])
            .WriteTo(Path.Combine(directory, "КУКРЫНИКСЫ", "Дорогая.kfn"));
        using var client = await LoginAsync();

        using var response = await client.GetAsync($"/api/songs/{songId}/{resource}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private void Build(string fileName, byte[] clip) =>
        new KfnFileBuilder()
            .WithEntry("Дорогая.mp3", 2, Audio)
            .WithEntry("фон.avi", 5, clip)
            .WithSongDefinition(Definition)
            .WriteTo(Path.Combine(directory, "КУКРЫНИКСЫ", fileName));

    private async Task<KaraokeScript> Script(HttpClient client) =>
        (await client.GetFromJsonAsync<KaraokeScript>($"/api/songs/{songId}/karaoke"))!;

    private ILibraryService Library => application.Services.GetRequiredService<ILibraryService>();

    private async Task<Guid> IndexAsync(string title)
    {
        await foreach (var _ in Library.RefreshRootAsync(rootId)) { }
        return Assert.Single(await Library.SearchSongsAsync(new(title))).Id;
    }

    private async Task<HttpClient> LoginAsync()
    {
        await application.CreateUserAsync("Viewer");
        var client = application.CreateSession();
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        request.Content = JsonContent.Create(new { login = "Viewer", password = AuthApplication.Password });
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return client;
    }
}
