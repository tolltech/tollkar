using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tollkar.Application.Library;
using Tollkar.Application.Library.Models;
using Tollkar.Application.Queue.Models;

namespace Tollkar.Web.Tests;

public sealed class CatalogTests : IAsyncLifetime
{
    private readonly AuthApplication application = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tollkar-catalog-" + Guid.NewGuid().ToString("N"));
    private LibrarySong[] songs = [];

    public async Task InitializeAsync()
    {
        await application.InitializeDatabaseAsync();
        Directory.CreateDirectory(directory);
        foreach (var title in new[] { "First", "Second", "Third" })
            await File.WriteAllBytesAsync(Path.Combine(directory, $"Artist - {title}.mp4"), [1]);
        var library = application.Services.GetRequiredService<ILibraryService>();
        var root = await library.AddRootAsync(directory);
        await foreach (var _ in library.RefreshRootAsync(root.Id)) { }
        songs = (await library.SearchSongsAsync(new())).ToArray();
    }

    public async Task DisposeAsync()
    {
        await application.DisposeAsync();
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task UsersCannotReadOrMutateEachOthersQueues()
    {
        using var alice = await RegisterAsync("Alice");
        using var bob = await RegisterAsync("Bob");
        foreach (var song in songs)
        {
            (await MutateAsync(alice, HttpMethod.Post, "/api/queue", new { songId = song.Id })).EnsureSuccessStatusCode();
            (await MutateAsync(bob, HttpMethod.Post, "/api/queue", new { songId = song.Id, userId = "Alice" })).EnsureSuccessStatusCode();
        }
        var first = await QueueAsync(alice);
        var second = await QueueAsync(bob);
        Assert.NotEqual(first[0].UserId, second[0].UserId);
        Assert.Equal(new[] { 0, 1, 2 }, second.Select(item => item.Position));
        Assert.Equal(second, await bob.GetFromJsonAsync<PlaybackQueueItem[]>("/api/queue?userId=" + first[0].UserId));

        (await MutateAsync(bob, HttpMethod.Post, $"/api/queue/{first[0].Id}/move", new { offset = 2 })).EnsureSuccessStatusCode();
        (await MutateAsync(bob, HttpMethod.Delete, $"/api/queue/{first[1].Id}")).EnsureSuccessStatusCode();
        Assert.Equal(first, await QueueAsync(alice));
        Assert.Equal(second, await QueueAsync(bob));

        (await MutateAsync(alice, HttpMethod.Post, $"/api/queue/{first[2].Id}/move", new { offset = int.MinValue })).EnsureSuccessStatusCode();
        Assert.Equal(new[] { first[2].Id, first[0].Id, first[1].Id }, (await QueueAsync(alice)).Select(item => item.Id));
        (await MutateAsync(alice, HttpMethod.Post, $"/api/queue/{first[2].Id}/move", new { offset = int.MaxValue })).EnsureSuccessStatusCode();
        (await MutateAsync(alice, HttpMethod.Delete, $"/api/queue/{first[1].Id}")).EnsureSuccessStatusCode();
        var remaining = await QueueAsync(alice);
        Assert.Equal(new[] { first[0].Id, first[2].Id }, remaining.Select(item => item.Id));
        Assert.Equal(new[] { 0, 1 }, remaining.Select(item => item.Position));
        Assert.Equal(second, await QueueAsync(bob));
    }

    [Fact]
    public async Task SearchReturnsMetadataWithoutLocalPaths()
    {
        using var client = await RegisterAsync("Alice");
        var result = await client.GetFromJsonAsync<LibrarySong[]>("/api/library/search?text=Fir&limit=1");
        Assert.Equal(songs[0], Assert.Single(result!));
        var json = await client.GetStringAsync("/api/library/search?text=Artist");
        Assert.DoesNotContain(directory, json);
        Assert.Equal(3, JsonDocument.Parse(json).RootElement.GetArrayLength());
        Assert.Empty((await client.GetFromJsonAsync<LibrarySong[]>("/api/library/search?text=Unknown"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/library/search?limit=501")).StatusCode);
    }

    [Fact]
    public async Task MutationsValidateSongsAndRequireCsrf()
    {
        using var client = await RegisterAsync("Alice");
        Assert.Equal(HttpStatusCode.NotFound,
            (await MutateAsync(client, HttpMethod.Post, "/api/queue", new { songId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await MutateAsync(client, HttpMethod.Post, "/api/queue", new { songId = Guid.Empty })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/queue", new { songId = songs[0].Id })).StatusCode);
        (await MutateAsync(client, HttpMethod.Post, "/api/queue", new { songId = songs[0].Id })).EnsureSuccessStatusCode();
        var item = Assert.Single(await QueueAsync(client));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/queue/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/queue/{item.Id}/move", new { offset = 1 })).StatusCode);
        Assert.Equal(item, Assert.Single(await QueueAsync(client)));
    }

    [Theory]
    [InlineData("GET", "/api/library/search")]
    [InlineData("GET", "/api/queue")]
    [InlineData("POST", "/api/queue")]
    [InlineData("DELETE", "/api/queue/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/queue/00000000-0000-0000-0000-000000000001/move")]
    public async Task AnonymousCatalogRequestsAreUnauthorized(string method, string path)
    {
        using var client = application.CreateSession();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = JsonContent.Create(new { songId = songs[0].Id, offset = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentAppendsKeepIndependentContiguousPositions()
    {
        using var alice = await RegisterAsync("Alice");
        using var bob = await RegisterAsync("Bob");
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            MutateAsync(index % 2 == 0 ? alice : bob, HttpMethod.Post,
                "/api/queue", new { songId = songs[0].Id })));
        foreach (var response in responses)
        {
            using (response) response.EnsureSuccessStatusCode();
        }
        foreach (var client in new[] { alice, bob })
        {
            var items = await QueueAsync(client);
            Assert.Equal(Enumerable.Range(0, 6), items.Select(item => item.Position));
            Assert.Equal(6, items.Select(item => item.Id).Distinct().Count());
        }
    }

    private async Task<HttpClient> RegisterAsync(string login)
    {
        var client = application.CreateSession();
        (await MutateAsync(client, HttpMethod.Post, "/api/auth/register", new { login, password = "Valid-password-42!" }))
            .EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<PlaybackQueueItem[]> QueueAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PlaybackQueueItem[]>("/api/queue"))!;

    private static async Task<HttpResponseMessage> MutateAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
