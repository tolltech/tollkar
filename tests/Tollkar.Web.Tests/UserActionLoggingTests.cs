using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Vostok.Logging.Abstractions;

namespace Tollkar.Web.Tests;

public sealed class UserActionLoggingTests : IAsyncLifetime
{
    private readonly RecordingLog log = new();
    private readonly AuthApplication application;

    public UserActionLoggingTests() => application = new(log: log);

    public Task InitializeAsync() => application.InitializeDatabaseAsync();
    public async Task DisposeAsync() => await application.DisposeAsync();

    [Fact]
    public async Task OnlySuccessfulLoginIsLogged()
    {
        await application.CreateUserAsync("Alice");
        using var client = application.CreateSession();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await MutateAsync(client, HttpMethod.Post, "/api/auth/login",
                new { login = "Alice", password = "incorrect" })).StatusCode);
        Assert.Empty(log.UserActions);

        (await MutateAsync(client, HttpMethod.Post, "/api/auth/login",
            new { login = "ALICE", password = AuthApplication.Password })).EnsureSuccessStatusCode();
        var login = Assert.Single(log.UserActions);
        Assert.Equal("Alice", login.Properties!["Login"]);
        Assert.Equal("POST", login.Properties["Method"]);
        Assert.Equal("/api/auth/login", login.Properties["Endpoint"]);
        Assert.Equal(200, login.Properties["StatusCode"]);

        log.Clear();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await MutateAsync(client, HttpMethod.Post, "/api/auth/login",
                new { login = "Alice", password = "incorrect" })).StatusCode);
        Assert.Empty(log.UserActions);
    }

    [Fact]
    public async Task OnlyExplicitApiActionsAreLoggedWithSafeParameters()
    {
        await application.CreateUserAsync("Alice");
        using var client = application.CreateSession();
        (await MutateAsync(client, HttpMethod.Post, "/api/auth/login",
            new { login = "Alice", password = AuthApplication.Password })).EnsureSuccessStatusCode();
        log.Clear();

        await client.GetAsync("/api/auth/me?source=menu");
        var id = Guid.NewGuid();
        (await MutateAsync(client, HttpMethod.Post,
            $"/api/queue/{id}/move?source=queue&token=secret", new { offset = 1 })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await MutateAsync(client, HttpMethod.Post, "/api/queue", new { songId = Guid.Empty })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await MutateRawAsync(client, "/api/queue", "{")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await MutateAsync(client, HttpMethod.Post, "/api/auth/register",
                new { login = "Bob", password = AuthApplication.Password })).StatusCode);
        (await MutateAsync(client, HttpMethod.Delete, $"/api/queue/{id}")).EnsureSuccessStatusCode();
        (await MutateAsync(client, HttpMethod.Post, $"/api/queue/{id}/play")).EnsureSuccessStatusCode();
        foreach (var command in new[] { "play", "pause", "seek", "next" })
            (await MutateAsync(client, HttpMethod.Post, "/api/queue/playback",
                new { action = command, revision = 0, positionSeconds = 0 })).EnsureSuccessStatusCode();
        (await MutateAsync(client, HttpMethod.Post, "/api/queue/playback",
            new { action = "ended", revision = 0, positionSeconds = 0 })).EnsureSuccessStatusCode();
        (await MutateAsync(client, HttpMethod.Post, "/api/auth/logout")).EnsureSuccessStatusCode();

        Assert.Equal(11, log.UserActions.Count);
        var action = Assert.Single(log.UserActions,
            logEvent => Equals(logEvent.Properties!["Endpoint"], "/api/queue/{id:guid}/move"));
        Assert.Equal("Alice", action.Properties!["Login"]);
        Assert.Equal("POST", action.Properties["Method"]);
        Assert.Equal("/api/queue/{id:guid}/move", action.Properties["Endpoint"]);
        Assert.Equal($"id={id}", action.Properties["RouteParameters"]);
        Assert.Equal("source=queue&token=REDACTED", action.Properties["QueryParameters"]);
        Assert.Equal(204, action.Properties["StatusCode"]);
        Assert.DoesNotContain("secret", action.Properties.Values.Select(value => value?.ToString()));
        var failed = log.UserActions
            .Where(logEvent => Equals(logEvent.Properties!["Endpoint"], "/api/queue/"))
            .ToArray();
        Assert.Equal(2, failed.Length);
        Assert.All(failed, logEvent => Assert.Equal(400, logEvent.Properties!["StatusCode"]));
        Assert.Single(log.UserActions, logEvent => Equals(logEvent.Properties!["Endpoint"], "/api/auth/register"));
        Assert.Single(log.UserActions, logEvent => Equals(logEvent.Properties!["Endpoint"], "/api/queue/{id:guid}"));
        Assert.Single(log.UserActions, logEvent => Equals(logEvent.Properties!["Endpoint"], "/api/queue/{id:guid}/play"));
        Assert.Equal(4, log.UserActions.Count(logEvent =>
            Equals(logEvent.Properties!["Endpoint"], "/api/queue/playback")));
        Assert.Single(log.UserActions, logEvent => Equals(logEvent.Properties!["Endpoint"], "/api/auth/logout"));
    }

    private static async Task<HttpResponseMessage> MutateAsync(HttpClient client, HttpMethod method,
        string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> MutateRawAsync(HttpClient client, string path, string body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private sealed class RecordingLog : ILog
    {
        private readonly ConcurrentQueue<LogEvent> events = new();

        public IReadOnlyList<LogEvent> UserActions => events
            .Where(logEvent => logEvent.MessageTemplate?.StartsWith("User action:", StringComparison.Ordinal) == true)
            .ToArray();

        public void Log(LogEvent @event) => events.Enqueue(@event);
        public bool IsEnabledFor(LogLevel level) => true;
        public ILog ForContext(string context) => this;
        public void Clear() => events.Clear();
    }
}
