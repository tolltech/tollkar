using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tollkar.Web.Authentication;

namespace Tollkar.Web.Tests;

public sealed class DevicePairingTests : IAsyncLifetime
{
    private readonly AdjustableTimeProvider time = new(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.FromHours(4)));
    private readonly AuthApplication application;

    public DevicePairingTests() => application = new(timeProvider: time);

    public Task InitializeAsync() => application.InitializeDatabaseAsync();
    public async Task DisposeAsync() => await application.DisposeAsync();

    [Fact]
    public async Task ConfirmedDisplayJoinsThePhoneQueueAsGuest()
    {
        await application.CreateUserAsync("Alice");
        using var phone = application.CreateSession();
        await AuthApplication.SignInAsync(phone, "Alice");
        var phoneQueue = await phone.GetFromJsonAsync<JsonElement>("/api/queue/test");

        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);

        var pending = await phone.GetFromJsonAsync<JsonElement>("/api/pairing/requests/" + UserCode(request));
        Assert.Equal("pending", pending.GetProperty("status").GetString());
        using var approval = await ApproveAsync(phone, UserCode(request));
        Assert.Equal(HttpStatusCode.NoContent, approval.StatusCode);

        using var session = await ClaimAsync(display, request);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal("approved", (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var current = await display.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(current.GetProperty("isGuest").GetBoolean());
        Assert.True(current.GetProperty("isDisplay").GetBoolean());
        var displayQueue = await display.GetFromJsonAsync<JsonElement>("/api/queue/test");
        Assert.Equal(phoneQueue.GetProperty("id").GetString(), displayQueue.GetProperty("id").GetString());
    }

    [Fact]
    public async Task DisplayStaysAnonymousUntilTheRequestIsConfirmed()
    {
        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);

        using var session = await ClaimAsync(display, request);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal("pending", (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await display.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ConfirmationGrantsExactlyOneSession()
    {
        await application.CreateUserAsync("Alice");
        using var phone = application.CreateSession();
        await AuthApplication.SignInAsync(phone, "Alice");

        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);
        (await ApproveAsync(phone, UserCode(request))).Dispose();
        (await ClaimAsync(display, request)).Dispose();

        using var replay = application.CreateSession();
        using var response = await ClaimAsync(replay, request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentClaimsShareOneConfirmation()
    {
        await application.CreateUserAsync("Alice");
        using var phone = application.CreateSession();
        await AuthApplication.SignInAsync(phone, "Alice");

        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);
        (await ApproveAsync(phone, UserCode(request))).Dispose();

        using var race = application.CreateSession();
        var claims = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => ClaimAsync(race, request)));
        try
        {
            Assert.Single(claims, claim => claim.StatusCode == HttpStatusCode.OK);
            Assert.All(claims.Where(claim => claim.StatusCode != HttpStatusCode.OK),
                claim => Assert.Equal(HttpStatusCode.NotFound, claim.StatusCode));
        }
        finally
        {
            foreach (var claim in claims) claim.Dispose();
        }
    }

    [Fact]
    public async Task SecondAccountCannotOverrideAConfirmation()
    {
        await application.CreateUserAsync("Alice");
        await application.CreateUserAsync("Bob");
        using var alice = application.CreateSession();
        using var bob = application.CreateSession();
        await AuthApplication.SignInAsync(alice, "Alice");
        await AuthApplication.SignInAsync(bob, "Bob");

        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);
        (await ApproveAsync(alice, UserCode(request))).Dispose();

        using var second = await ApproveAsync(bob, UserCode(request));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var repeat = await ApproveAsync(alice, UserCode(request));
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);

        using var session = await ClaimAsync(display, request);
        session.EnsureSuccessStatusCode();
        var aliceQueue = await alice.GetFromJsonAsync<JsonElement>("/api/queue/test");
        var displayQueue = await display.GetFromJsonAsync<JsonElement>("/api/queue/test");
        Assert.Equal(aliceQueue.GetProperty("id").GetString(), displayQueue.GetProperty("id").GetString());
    }

    [Fact]
    public async Task DeclinedRequestCannotBeConfirmedLater()
    {
        await application.CreateUserAsync("Alice");
        using var phone = application.CreateSession();
        await AuthApplication.SignInAsync(phone, "Alice");

        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);
        using var decline = await DeclineAsync(phone, UserCode(request));
        Assert.Equal(HttpStatusCode.NoContent, decline.StatusCode);

        using var approval = await ApproveAsync(phone, UserCode(request));
        Assert.Equal(HttpStatusCode.NotFound, approval.StatusCode);
        using var session = await ClaimAsync(display, request);
        Assert.Equal(HttpStatusCode.NotFound, session.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await display.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task VisibleUserCodeAloneDoesNotGrantASession()
    {
        await application.CreateUserAsync("Alice");
        using var phone = application.CreateSession();
        await AuthApplication.SignInAsync(phone, "Alice");

        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);
        (await ApproveAsync(phone, UserCode(request))).Dispose();

        using var onlooker = application.CreateSession();
        using var response = await ClaimAsync(onlooker, UserCode(request), "0123456789abcdef0123456789abcdef");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await onlooker.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task RequestExpiresAfterItsLifetime()
    {
        await application.CreateUserAsync("Alice");
        using var phone = application.CreateSession();
        await AuthApplication.SignInAsync(phone, "Alice");

        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);
        time.Advance(DevicePairing.Lifetime + TimeSpan.FromSeconds(1));

        using var approval = await ApproveAsync(phone, UserCode(request));
        Assert.Equal(HttpStatusCode.Gone, approval.StatusCode);
        using var session = await ClaimAsync(display, request);
        Assert.Equal(HttpStatusCode.Gone, session.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await display.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task GuestSessionCannotConfirmAnotherDevice()
    {
        await application.CreateUserAsync("Alice");
        using var owner = application.CreateSession();
        await AuthApplication.SignInAsync(owner, "Alice");
        var access = await owner.GetFromJsonAsync<JsonElement>("/api/guest/access");

        using var guest = application.CreateSession();
        (await guest.GetAsync(access.GetProperty("url").GetString())).Dispose();

        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await guest.GetAsync("/api/pairing/requests/" + UserCode(request))).StatusCode);
        using var approval = await ApproveAsync(guest, UserCode(request));
        Assert.Equal(HttpStatusCode.Forbidden, approval.StatusCode);
    }

    [Fact]
    public async Task ConfirmationRequiresAntiforgeryToken()
    {
        await application.CreateUserAsync("Alice");
        using var phone = application.CreateSession();
        await AuthApplication.SignInAsync(phone, "Alice");

        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);

        using var approval = await phone.PostAsync(
            "/api/pairing/requests/" + UserCode(request) + "/approve", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, approval.StatusCode);
        using var session = await ClaimAsync(display, request);
        Assert.Equal("pending", (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task SecondAccountCannotRevokeAConfirmation()
    {
        await application.CreateUserAsync("Alice");
        await application.CreateUserAsync("Bob");
        using var alice = application.CreateSession();
        using var bob = application.CreateSession();
        await AuthApplication.SignInAsync(alice, "Alice");
        await AuthApplication.SignInAsync(bob, "Bob");

        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);
        (await ApproveAsync(alice, UserCode(request))).Dispose();

        using var decline = await DeclineAsync(bob, UserCode(request));
        Assert.Equal(HttpStatusCode.Conflict, decline.StatusCode);
        using var session = await ClaimAsync(display, request);
        session.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task QrCodeIsServedWhileTheRequestLives()
    {
        await application.CreateUserAsync("Alice");
        using var phone = application.CreateSession();
        await AuthApplication.SignInAsync(phone, "Alice");

        using var display = application.CreateSession();
        var request = await CreateRequestAsync(display);

        using var pending = await display.GetAsync("/api/pairing/qr?code=" + UserCode(request));
        pending.EnsureSuccessStatusCode();
        Assert.Equal("image/svg+xml", pending.Content.Headers.ContentType?.MediaType);

        (await ApproveAsync(phone, UserCode(request))).Dispose();
        using var confirmed = await display.GetAsync("/api/pairing/qr?code=" + UserCode(request));
        confirmed.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await display.GetAsync("/api/pairing/qr?code=missing")).StatusCode);
    }

    private static async Task<JsonElement> CreateRequestAsync(HttpClient display)
    {
        using var response = await display.PostAsync("/api/pairing/requests", content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> ApproveAsync(HttpClient phone, string? userCode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/pairing/requests/" + userCode + "/approve");
        request.Headers.Add("X-CSRF-TOKEN", await AuthApplication.CsrfTokenAsync(phone));
        return await phone.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeclineAsync(HttpClient phone, string? userCode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/pairing/requests/" + userCode);
        request.Headers.Add("X-CSRF-TOKEN", await AuthApplication.CsrfTokenAsync(phone));
        return await phone.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ClaimAsync(HttpClient display, JsonElement request) =>
        ClaimAsync(display, UserCode(request), request.GetProperty("deviceCode").GetString());

    private static Task<HttpResponseMessage> ClaimAsync(HttpClient display, string? userCode, string? deviceCode) =>
        display.PostAsJsonAsync("/api/pairing/session", new { userCode, deviceCode });

    private static string? UserCode(JsonElement request) => request.GetProperty("userCode").GetString();
}
