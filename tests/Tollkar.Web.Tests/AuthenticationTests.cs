using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Tollkar.Web.Tests;

public sealed class AuthenticationTests : IAsyncLifetime
{
    private readonly AuthApplication application = new();
    private const string Password = "Test-Password7!";

    public Task InitializeAsync() => application.InitializeDatabaseAsync();
    public async Task DisposeAsync() => await application.DisposeAsync();

    [Fact]
    public async Task RegisterCreatesSessionWithSecureCookie()
    {
        using var client = application.CreateSession();
        using var response = await PostAsync(client, "register", "Alice");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("Tollkar.Auth="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Alice", user.GetProperty("login").GetString());
        Assert.False(string.IsNullOrEmpty(user.GetProperty("id").GetString()));
        Assert.Equal(2, user.EnumerateObject().Count());
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(user.GetRawText(), me.GetRawText());
    }

    [Fact]
    public async Task DuplicateNormalizedLoginIsRejected()
    {
        using var first = application.CreateSession();
        using var second = application.CreateSession();
        (await PostAsync(first, "register", "Alice")).EnsureSuccessStatusCode();
        using var response = await PostAsync(second, "register", "aLiCe");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, "DuplicateUserName");
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task LoginUsesNormalizedNameAndLogoutClearsSession()
    {
        using var client = application.CreateSession();
        (await PostAsync(client, "register", "Alice")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/queue/test")).StatusCode);
        (await PostAsync(client, "login", "ALICE")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/queue/test")).StatusCode);
    }

    [Theory]
    [InlineData("Alice", "incorrect")]
    [InlineData("Unknown", Password)]
    public async Task InvalidLoginDoesNotAuthenticate(string login, string password)
    {
        using var owner = application.CreateSession();
        using var client = application.CreateSession();
        (await PostAsync(owner, "register", "Alice")).EnsureSuccessStatusCode();
        using var response = await PostAsync(client, "login", login, password);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorAsync(response, "InvalidCredentials");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/me", HttpStatusCode.Unauthorized)]
    [InlineData("/api/queue/test", HttpStatusCode.Unauthorized)]
    [InlineData("/api/health", HttpStatusCode.OK)]
    [InlineData("/api/unknown", HttpStatusCode.NotFound)]
    [InlineData("/api/unknown.json", HttpStatusCode.NotFound)]
    public async Task AnonymousApiNeverRedirectsToSpa(string path, HttpStatusCode expected)
    {
        using var client = application.CreateSession();
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("text/html", response.Content.Headers.ContentType?.ToString() ?? "");
    }

    [Fact]
    public async Task ForbiddenDoesNotRedirect()
    {
        using var client = application.CreateSession();
        (await PostAsync(client, "register", "Alice")).EnsureSuccessStatusCode();
        using var response = await client.GetAsync("/api/queue/forbidden");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task SessionsOnlyExposeTheirOwnUser()
    {
        using var alice = application.CreateSession();
        using var bob = application.CreateSession();
        (await PostAsync(alice, "register", "Alice")).EnsureSuccessStatusCode();
        (await PostAsync(bob, "register", "Bob")).EnsureSuccessStatusCode();
        var first = await alice.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var second = await bob.GetFromJsonAsync<JsonElement>("/api/auth/me?id=" + first.GetProperty("id").GetString());
        Assert.Equal("Bob", second.GetProperty("login").GetString());
        Assert.NotEqual(first.GetProperty("id").GetString(), second.GetProperty("id").GetString());
        var protectedData = await bob.GetFromJsonAsync<JsonElement>("/api/queue/test");
        Assert.Equal(second.GetProperty("id").GetString(), protectedData.GetProperty("id").GetString());
        (await PostAsync(alice, "logout")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("", Password, "Login")]
    [InlineData("Alice", "", "Password")]
    [InlineData("Alice", "weak", "PasswordRequiresNonAlphanumeric")]
    public async Task ValidationErrorsHaveStableJson(string login, string password, string code)
    {
        using var client = application.CreateSession();
        using var response = await PostAsync(client, "register", login, password);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, code);
    }

    [Fact]
    public async Task MissingCsrfTokenRejectsMutation()
    {
        using var client = application.CreateSession();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new { login = "Alice", password = Password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, "Csrf");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    public async Task MalformedJsonUsesValidationContract(string body)
    {
        using var client = application.CreateSession();
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        using var response = await client.PostAsync("/api/auth/register", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorAsync(response, "Request");
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string action,
        string login = "Alice", string password = Password)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/" + action);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        if (action != "logout") request.Content = JsonContent.Create(new { login, password });
        return await client.SendAsync(request);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string code)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)response.StatusCode, json.GetProperty("status").GetInt32());
        Assert.True(json.GetProperty("errors").TryGetProperty(code, out _));
        Assert.DoesNotContain(Password, json.GetRawText());
    }

}
