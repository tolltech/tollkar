using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tollkar.Web.Authentication;
using Tollkar.Web.Persistence;
using Vostok.Logging.Abstractions;

namespace Tollkar.Web.Tests;

public sealed class AuthApplication : WebApplicationFactory<Program>
{
    public const string Password = "Valid-password-42!";
    private readonly SaveChangesInterceptor? interceptor;
    private readonly ILog? log;
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tollkar-auth-" + Guid.NewGuid().ToString("N"));

    public AuthApplication(SaveChangesInterceptor? interceptor = null, ILog? log = null)
    {
        this.interceptor = interceptor;
        this.log = log;
        Directory.CreateDirectory(directory);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebDatabase"] = $"Data Source={Path.Combine(directory, "web.db")};Pooling=False",
                ["Library:DatabasePath"] = Path.Combine(directory, "library.db"),
                ["Library:SongsPath"] = Path.Combine(directory, "songs"),
                ["VostokLogging:FilePath"] = Path.Combine(directory, "web.log")
            }));
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ProtectedTestEndpoints>());
        if (log is not null)
            builder.ConfigureServices(services => services.AddSingleton<ILog>(log));
        if (interceptor is not null)
            builder.ConfigureServices(services => services.ConfigureDbContext<WebDbContext>(
                options => options.AddInterceptors(interceptor)));
    }

    public async Task InitializeDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<WebDbContext>().Database;
        Assert.Equal(Path.Combine(directory, "web.db"), database.GetDbConnection().DataSource);
        await database.MigrateAsync();
    }

    public HttpClient CreateSession() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
    });

    public async Task CreateUserAsync(string login, string password = Password)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TollkarUser>>();
        var result = await users.CreateAsync(new TollkarUser { UserName = login }, password);
        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(error => error.Description)));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Directory.Delete(directory, recursive: true);
    }

    private sealed class ProtectedTestEndpoints : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseEndpoints(endpoints =>
            {
                // No explicit policy: proves future endpoints inherit authentication by default.
                endpoints.MapGet("/api/queue/test", (HttpContext context) => Results.Ok(new
                {
                    id = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                }));
                endpoints.MapGet("/api/queue/forbidden", () => Results.Ok())
                    .RequireAuthorization(policy => policy.RequireClaim("test-permission"));
            });
        };
    }
}
