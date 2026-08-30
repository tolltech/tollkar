using System.Security.Claims;
using Tollkar.Application.Library;
using Tollkar.Application.Queue;
using Tollkar.Infrastructure;

namespace Tollkar.Web.Catalog;

public static class CatalogServices
{
    public static void AddCatalog(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ILibraryService>(services =>
            TollkarInfrastructure.CreateLibraryService(DatabasePath(services)));
        builder.Services.AddScoped<IPlaybackQueueService>(services =>
        {
            var userId = services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User
                .FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("An authenticated user is required.");
            return TollkarInfrastructure.CreateServices(DatabasePath(services), userId).PlaybackQueue;
        });
    }

    private static string DatabasePath(IServiceProvider services) =>
        services.GetRequiredService<IConfiguration>()["Library:DatabasePath"]
        ?? throw new InvalidOperationException("Library:DatabasePath is required.");
}
