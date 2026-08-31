using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tollkar.Web.Authentication;

namespace Tollkar.Web.Tests;

public sealed class ConcurrentRegistrationTests
{
    [Fact]
    public async Task ConcurrentNormalizedRegistrationsReturnValidationError()
    {
        await using var application = new AuthApplication(new RegistrationBarrier());
        await application.InitializeDatabaseAsync();
        await application.CreateUserAsync("admin");
        using var first = application.CreateSession();
        using var second = application.CreateSession();
        await LoginAsync(first);
        await LoginAsync(second);
        await SetCsrfAsync(first);
        await SetCsrfAsync(second);
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/auth/register", new { login = "Alice", password = "Test-Password7!" }),
            second.PostAsJsonAsync("/api/auth/register", new { login = "ALICE", password = "Test-Password7!" }));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
            var json = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(json.GetProperty("errors").TryGetProperty("DuplicateUserName", out _));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private static async Task SetCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        request.Content = JsonContent.Create(new { login = "admin", password = AuthApplication.Password });
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class RegistrationBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<TollkarUser>().Any(entry =>
                    entry.State == EntityState.Added && entry.Entity.UserName != "admin") == true)
            {
                // Both Identity uniqueness checks must finish before either insert is allowed.
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
