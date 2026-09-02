using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tollkar.Web.Tests;

public sealed class GuestAccessTests : IAsyncLifetime
{
    private readonly AdjustableTimeProvider time = new(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.FromHours(4)));
    private readonly AuthApplication application;

    public GuestAccessTests() => application = new(timeProvider: time);

    public Task InitializeAsync() => application.InitializeDatabaseAsync();
    public async Task DisposeAsync() => await application.DisposeAsync();

    [Fact]
    public async Task GuestUsesOwnersQueueWithoutAccount()
    {
        await application.CreateUserAsync("Alice");
        using var owner = application.CreateSession();
        await LoginAsync(owner, "Alice");
        var ownerState = await owner.GetFromJsonAsync<JsonElement>("/api/queue/test");
        var access = await owner.GetFromJsonAsync<JsonElement>("/api/guest/access");

        using var guest = application.CreateSession();
        using var enter = await guest.GetAsync(access.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.Redirect, enter.StatusCode);
        Assert.Equal("/queue", enter.Headers.Location?.OriginalString);

        var current = await guest.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(current.GetProperty("isGuest").GetBoolean());
        Assert.Equal("Гость", current.GetProperty("login").GetString());
        var guestState = await guest.GetFromJsonAsync<JsonElement>("/api/queue/test");
        Assert.Equal(ownerState.GetProperty("id").GetString(), guestState.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync("/api/guest/access")).StatusCode);
    }

    [Fact]
    public async Task GuestLinkExpiresOnNextServerDate()
    {
        await application.CreateUserAsync("Alice");
        using var owner = application.CreateSession();
        await LoginAsync(owner, "Alice");
        var access = await owner.GetFromJsonAsync<JsonElement>("/api/guest/access");
        time.Advance(TimeSpan.FromDays(1));

        using var guest = application.CreateSession();
        using var response = await guest.GetAsync(access.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?guest=expired", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task GuestLinkReplacesExistingAccountSession()
    {
        await application.CreateUserAsync("Alice");
        await application.CreateUserAsync("Bob");
        using var alice = application.CreateSession();
        await LoginAsync(alice, "Alice");
        var access = await alice.GetFromJsonAsync<JsonElement>("/api/guest/access");

        using var bob = application.CreateSession();
        await LoginAsync(bob, "Bob");
        using var enter = await bob.GetAsync(access.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.Redirect, enter.StatusCode);

        var current = await bob.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(current.GetProperty("isGuest").GetBoolean());
        var queue = await bob.GetFromJsonAsync<JsonElement>("/api/queue/test");
        Assert.Equal(current.GetProperty("id").GetString(), queue.GetProperty("id").GetString());
        var aliceQueue = await alice.GetFromJsonAsync<JsonElement>("/api/queue/test");
        Assert.Equal(aliceQueue.GetProperty("id").GetString(), queue.GetProperty("id").GetString());
    }

    private static async Task LoginAsync(HttpClient client, string login)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        request.Content = JsonContent.Create(new { login, password = AuthApplication.Password });
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone(
            "Test", now.Offset, "Test", "Test");
        public override DateTimeOffset GetUtcNow() => current.ToUniversalTime();
        public void Advance(TimeSpan interval) => current = current.Add(interval);
    }
}
