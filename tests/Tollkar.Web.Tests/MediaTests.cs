using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tollkar.Application.Library;
using Tollkar.Web.Catalog;

namespace Tollkar.Web.Tests;

public sealed class MediaTests : IAsyncLifetime
{
    private static readonly byte[] Video = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
    private readonly AuthApplication application = new();
    private string directory = null!;
    private string file = null!;
    private Guid songId;

    public async Task InitializeAsync()
    {
        await application.InitializeDatabaseAsync();
        directory = application.Services.GetRequiredService<IOptions<LibrarySyncOptions>>().Value.SongsPath;
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        file = Path.Combine(directory, "nested", "Artist - Video.MP4");
        await File.WriteAllBytesAsync(file, Video);
        songId = await IndexAsync(directory, "Video");
    }

    public async Task DisposeAsync() => await application.DisposeAsync();

    [Fact]
    public async Task StreamsIndexedVideoWithoutAcceptingClientPaths()
    {
        using var client = await LoginAsync();
        using var response = await client.GetAsync($"{MediaUrl}?path=/etc/passwd&filePath=../web.db");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Video.Length, response.Content.Headers.ContentLength);
        Assert.Equal(Video, await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("bytes", response.Headers.AcceptRanges);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Null(response.Content.Headers.ContentDisposition);
    }

    [Theory]
    [InlineData("bytes=10-19", 10, 19)]
    [InlineData("bytes=250-", 250, 255)]
    [InlineData("bytes=-5", 251, 255)]
    [InlineData("bytes=250-999", 250, 255)]
    public async Task StreamsRequestedRange(string range, int first, int last)
    {
        using var client = await LoginAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, MediaUrl);
        request.Headers.Add("Range", range);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal($"bytes {first}-{last}/{Video.Length}", response.Content.Headers.ContentRange?.ToString());
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(last - first + 1, response.Content.Headers.ContentLength);
        Assert.Equal(Video[first..(last + 1)], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UnsatisfiableRangeReturnsFileLength()
    {
        using var client = await LoginAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, MediaUrl);
        request.Headers.Add("Range", "bytes=256-");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
        Assert.Equal("bytes */256", response.Content.Headers.ContentRange?.ToString());
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("bytes=invalid")]
    [InlineData("bytes=0-1,4-5")]
    public async Task UnsupportedRangesFallBackToWholeVideo(string range)
    {
        using var client = await LoginAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, MediaUrl);
        request.Headers.TryAddWithoutValidation("Range", range);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Video, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task HeadReturnsHeadersWithoutVideo()
    {
        using var client = await LoginAsync();
        using var request = new HttpRequestMessage(HttpMethod.Head, MediaUrl);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Video.Length, response.Content.Headers.ContentLength);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task AnonymousRequestsCannotReadMedia(string method)
    {
        using var client = application.CreateSession();
        using var request = new HttpRequestMessage(new HttpMethod(method), MediaUrl);
        request.Headers.Add("Range", "bytes=0-1");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task MissingSongsAndDeletedFilesReturnNotFound()
    {
        using var client = await LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/songs/{Guid.NewGuid()}/media")).StatusCode);
        File.Delete(file);
        using var response = await client.GetAsync(MediaUrl);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(directory, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task EmptySongIdReturnsNotFound(string method)
    {
        using var client = await LoginAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/songs/{Guid.Empty}/media");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CatalogEntriesOutsideConfiguredRootAreNotServed()
    {
        // A sibling with a shared prefix must not pass the root containment check.
        var outside = directory + "-private";
        Directory.CreateDirectory(outside);
        await File.WriteAllBytesAsync(Path.Combine(outside, "Outside.mp4"), Video);
        var outsideId = await IndexAsync(outside, "Outside");
        using var client = await LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/songs/{outsideId}/media")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacedFileOrDirectorySymlinksAreNotServed(bool directoryLink)
    {
        var outside = directory + "-private";
        Directory.CreateDirectory(outside);
        var target = Path.Combine(outside, Path.GetFileName(file));
        await File.WriteAllBytesAsync(target, Video);
        File.Delete(file);
        if (directoryLink)
        {
            Directory.Delete(Path.GetDirectoryName(file)!);
            Directory.CreateSymbolicLink(Path.GetDirectoryName(file)!, outside);
        }
        else
            File.CreateSymbolicLink(file, target);

        using var client = await LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(MediaUrl)).StatusCode);
    }

    [Theory]
    [InlineData("/api/songs/not-a-guid/media")]
    [InlineData("/api/songs/..%2Fweb.db/media")]
    [InlineData("/songs/nested/Artist%20-%20Video.MP4")]
    public async Task PathsDoNotExposeMedia(string url)
    {
        using var client = await LoginAsync();
        using var response = await client.GetAsync(url);
        Assert.NotEqual("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual(Video, await response.Content.ReadAsByteArrayAsync());
    }

    private string MediaUrl => $"/api/songs/{songId}/media";

    private async Task<Guid> IndexAsync(string rootPath, string title)
    {
        var library = application.Services.GetRequiredService<ILibraryService>();
        var root = await library.AddRootAsync(rootPath);
        await foreach (var _ in library.RefreshRootAsync(root.Id)) { }
        return Assert.Single(await library.SearchSongsAsync(new(title))).Id;
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
