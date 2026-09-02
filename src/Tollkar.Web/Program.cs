using Tollkar.Web.Authentication;
using Tollkar.Web.Catalog;
using Tollkar.Application.Library;
using Tollkar.Web.Realtime;
using Tollkar.Web.Media;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Tollkar.Web.Persistence;
using Tollkar.Web.Logging;

var migrateDatabases = args.Length == 1 && args[0] == "--migrate-databases";
var builder = WebApplication.CreateBuilder(migrateDatabases ? [] : args);
builder.AddWebLogging();
builder.AddWebAuthentication();
builder.AddCatalog();
builder.Services.AddSignalR();
builder.Services.AddSingleton<QueueStateCoordinator>();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
await using var app = builder.Build();
if (migrateDatabases)
{
    // Deployment updates both schemas without starting HTTP or the song scanner.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<WebDbContext>().Database.MigrateAsync();
    await app.Services.GetRequiredService<ILibraryService>().InitializeAsync();
    return;
}
await app.Services.GetRequiredService<ILibraryService>().InitializeAsync();

// The default trusted proxies are loopback addresses, where local Caddy runs.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseStatusCodePages(async context =>
{
    if (context.HttpContext.Request.Path.StartsWithSegments("/api") &&
        context.HttpContext.Response.StatusCode is 400 or 415)
        await Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Request"] = ["Ожидается корректный JSON-запрос."]
        }, statusCode: context.HttpContext.Response.StatusCode).ExecuteAsync(context.HttpContext);
});
app.UseAuthentication();
app.UseUserActionLogging();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapGuestAccessEndpoints();
app.MapCatalogEndpoints();
app.MapMediaEndpoints();
app.MapHub<KaraokeHub>("/api/karaoke");
app.Map("/api/{**path}", () => Results.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program;
