using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tollkar.Web.Persistence;

namespace Tollkar.Web.Tests;

public sealed class AuthApplication : WebApplicationFactory<Program>
{
    private readonly SaveChangesInterceptor? interceptor;
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tollkar-auth-" + Guid.NewGuid().ToString("N"));

    public AuthApplication(SaveChangesInterceptor? interceptor = null)
    {
        this.interceptor = interceptor;
        Directory.CreateDirectory(directory);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebDatabase"] = $"Data Source={Path.Combine(directory, "web.db")};Pooling=False"
            }));
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ProtectedTestEndpoints>());
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
