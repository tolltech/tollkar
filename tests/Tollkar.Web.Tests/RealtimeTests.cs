using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tollkar.Application.Library;
using Tollkar.Web.Realtime;

namespace Tollkar.Web.Tests;

public sealed class RealtimeTests : IAsyncLifetime
{
    private readonly AuthApplication application = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tollkar-realtime-" + Guid.NewGuid().ToString("N"));
    private Guid songId;
    private Guid rootId;

    public async Task InitializeAsync()
    {
        await application.InitializeDatabaseAsync();
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "Artist - Song.mp4"), [1]);
        var library = application.Services.GetRequiredService<ILibraryService>();
        var root = await library.AddRootAsync(directory);
        rootId = root.Id;
        await foreach (var _ in library.RefreshRootAsync(root.Id)) { }
        songId = Assert.Single(await library.SearchSongsAsync(new())).Id;
    }

    public async Task DisposeAsync()
    {
        await application.DisposeAsync();
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task ConnectionsReceiveVersionedChangesOnlyForTheirAuthenticatedUser()
    {
        var (alice, cookie) = await RegisterAsync("Alice");
        using var aliceSession = alice;
        var (bob, bobCookie) = await RegisterAsync("Bob");
        using var bobSession = bob;
        using var first = await ConnectAsync(cookie);
        using var second = await ConnectAsync(cookie);
        using var other = await ConnectAsync(bobCookie);
        Assert.Empty((await first.SnapshotAsync()).Items);
        Assert.Empty((await second.SnapshotAsync()).Items);
        Assert.Empty((await other.SnapshotAsync()).Items);

        await MutateAsync(alice, HttpMethod.Post, "/api/queue", new { songId });
        var added = await first.ChangedAsync();
        Assert.Equal(added.Items, (await second.ChangedAsync()).Items);
        var item = Assert.Single(added.Items);
        await MutateAsync(alice, HttpMethod.Post, "/api/queue", new { songId });
        var appended = await first.ChangedAsync();
        await second.ChangedAsync();
        Assert.True(appended.Version > added.Version);
        Assert.Equal(2, appended.Items.Count);

        await MutateAsync(alice, HttpMethod.Post, $"/api/queue/{item.Id}/move", new { offset = 1 });
        var moved = await first.ChangedAsync();
        await second.ChangedAsync();
        Assert.Equal(item.Id, moved.Items[1].Id);
        Assert.True(moved.Version > appended.Version);
        await MutateAsync(alice, HttpMethod.Delete, $"/api/queue/{item.Id}");
        var removed = await first.ChangedAsync();
        await second.ChangedAsync();
        Assert.Single(removed.Items);
        Assert.True(removed.Version > moved.Version);
        // A completion is the next message: no Alice event was sent to Bob's connection.
        Assert.Empty((await other.SnapshotAsync()).Items);
    }

    [Fact]
    public async Task ReconnectingRestoresMissedChangesAndRejoinsUserGroup()
    {
        var (client, cookie) = await RegisterAsync("Alice");
        using var session = client;
        using var original = await ConnectAsync(cookie);
        var before = await original.SnapshotAsync();
        original.Dispose();
        await MutateAsync(client, HttpMethod.Post, "/api/queue", new { songId });
        using var reconnected = await ConnectAsync(cookie);
        var restored = await reconnected.SnapshotAsync();
        Assert.Single(restored.Items);
        Assert.True(restored.Version > before.Version);
        await MutateAsync(client, HttpMethod.Delete, $"/api/queue/{restored.Items[0].Id}");
        var changed = await reconnected.ChangedAsync();
        Assert.Empty(changed.Items);
        Assert.True(changed.Version > restored.Version);
    }

    [Fact]
    public async Task ConcurrentMutationsPublishIncreasingVersionsWithMatchingSnapshots()
    {
        var (client, cookie) = await RegisterAsync("Alice");
        using var session = client;
        using var connection = await ConnectAsync(cookie);
        var previous = await connection.SnapshotAsync();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            MutateAsync(client, HttpMethod.Post, "/api/queue", new { songId })));
        var events = new List<QueueSnapshot>();
        for (var index = 0; index < 8; index++) events.Add(await connection.ChangedAsync());
        var count = 0;
        foreach (var current in events.OrderBy(value => value.Version))
        {
            count++;
            Assert.True(current.Version > previous.Version);
            Assert.Equal(count, current.Items.Count);
            Assert.Equal(Enumerable.Range(0, count), current.Items.Select(item => item.Position));
            previous = current;
        }
        var snapshot = await connection.SnapshotAsync();
        Assert.True(snapshot.Version >= previous.Version);
        Assert.Equal(previous.Items, snapshot.Items);
    }

    [Fact]
    public async Task LibraryRefreshInvalidatesQueueAndSnapshotReflectsDeletedSongs()
    {
        var (client, cookie) = await RegisterAsync("Alice");
        using var session = client;
        using var connection = await ConnectAsync(cookie);
        await connection.SnapshotAsync();
        await MutateAsync(client, HttpMethod.Post, "/api/queue", new { songId });
        var before = await connection.ChangedAsync();
        File.Delete(Path.Combine(directory, "Artist - Song.mp4"));
        var library = application.Services.GetRequiredService<ILibraryService>();
        await foreach (var _ in library.RefreshRootAsync(rootId)) { }
        await connection.InvalidatedAsync();
        var after = await connection.SnapshotAsync();
        Assert.Empty(after.Items);
        Assert.True(after.Version > before.Version);
    }

    [Fact]
    public async Task AnonymousNegotiationIsUnauthorized()
    {
        using var client = application.CreateSession();
        using var response = await client.PostAsync("/api/karaoke/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(HttpClient Client, string Cookie)> RegisterAsync(string login)
    {
        var client = application.CreateSession();
        using var response = await SendAsync(client, HttpMethod.Post, "/api/auth/register",
            new { login, password = "Valid-password-42!" });
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("Tollkar.Auth=", StringComparison.Ordinal)).Split(';')[0];
        return (client, cookie);
    }

    private async Task<HubSocket> ConnectAsync(string cookie)
    {
        var client = application.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Cookie = cookie;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var socket = new HubSocket(await client.ConnectAsync(new Uri("wss://localhost/api/karaoke"), timeout.Token));
        await socket.HandshakeAsync();
        return socket;
    }

    private static async Task MutateAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using var response = await SendAsync(client, method, path, body);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private sealed class HubSocket(WebSocket socket) : IDisposable
    {
        private readonly Queue<string> messages = new();
        private string pending = "";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task HandshakeAsync()
        {
            await SendAsync(new { protocol = "json", version = 1 });
            Assert.Equal("{}", (await ReceiveAsync()).GetRawText());
        }

        public async Task<QueueSnapshot> SnapshotAsync()
        {
            await SendAsync(new { type = 1, invocationId = "snapshot", target = "GetSnapshot", arguments = Array.Empty<object>() });
            var message = await ReceiveAsync();
            Assert.Equal(3, message.GetProperty("type").GetInt32());
            return message.GetProperty("result").Deserialize<QueueSnapshot>(JsonOptions)!;
        }

        public async Task InvalidatedAsync()
        {
            var message = await ReceiveAsync();
            Assert.Equal("QueueInvalidated", message.GetProperty("target").GetString());
            Assert.Empty(message.GetProperty("arguments").EnumerateArray());
        }

        public async Task<QueueSnapshot> ChangedAsync()
        {
            var message = await ReceiveAsync();
            Assert.Equal("QueueChanged", message.GetProperty("target").GetString());
            return message.GetProperty("arguments")[0].Deserialize<QueueSnapshot>(JsonOptions)!;
        }

        private async Task SendAsync(object value)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + "\u001e"),
                WebSocketMessageType.Text, true, timeout.Token);
        }

        private async Task<JsonElement> ReceiveAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var buffer = new byte[65536];
            while (true)
            {
                while (messages.Count == 0)
                {
                    var result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                    Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
                    pending += Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var frames = pending.Split('\u001e');
                    foreach (var frame in frames[..^1]) messages.Enqueue(frame);
                    pending = frames[^1];
                }
                using var document = JsonDocument.Parse(messages.Dequeue());
                var message = document.RootElement.Clone();
                if (message.TryGetProperty("type", out var type) && type.GetInt32() == 6) continue;
                return message;
            }
        }

        public void Dispose() => socket.Dispose();
    }
}
