using System.Security.Claims;
using Tollkar.Application.Library;
using Tollkar.Application.Queue;
using Tollkar.Infrastructure;
using Tollkar.Web.Realtime;

namespace Tollkar.Web.Catalog;

public static class CatalogServices
{
    public static void AddCatalog(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ILibraryService>(services =>
            new SynchronizedLibrary(TollkarInfrastructure.CreateLibraryService(DatabasePath(services)),
                services.GetRequiredService<QueueStateCoordinator>()));
        builder.Services.AddScoped<SynchronizedPlaybackQueue>(services =>
        {
            var userId = services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User
                .FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("An authenticated user is required.");
            return new SynchronizedPlaybackQueue(userId,
                TollkarInfrastructure.CreateServices(DatabasePath(services), userId).PlaybackQueue,
                services.GetRequiredService<QueueStateCoordinator>());
        });
        builder.Services.AddScoped<IPlaybackQueueService>(services =>
            services.GetRequiredService<SynchronizedPlaybackQueue>());
    }

    private static string DatabasePath(IServiceProvider services) =>
        services.GetRequiredService<IConfiguration>()["Library:DatabasePath"]
        ?? throw new InvalidOperationException("Library:DatabasePath is required.");
}
