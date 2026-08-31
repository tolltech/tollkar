using Microsoft.Extensions.Logging;
using Vostok.Logging.Abstractions;
using Vostok.Logging.File;
using Vostok.Logging.File.Configuration;
using Vostok.Logging.Microsoft;
using VostokLogLevel = Vostok.Logging.Abstractions.LogLevel;

namespace Tollkar.Web.Logging;

public static class WebLogging
{
    private const long MaxFileSize = 100 * 1024 * 1024;
    private const int MaxFiles = 5;

    public static void AddWebLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ILog>(CreateFileLog);
        builder.Services.AddSingleton<ILoggerProvider>(provider =>
            new VostokLoggerProvider(provider.GetRequiredService<ILog>()));
    }

    private static ILog CreateFileLog(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var environment = provider.GetRequiredService<IHostEnvironment>();
        var configuredPath = configuration["VostokLogging:FilePath"] ?? "logs/web.log";
        var filePath = Path.GetFullPath(configuredPath, environment.ContentRootPath);
        return new FileLog(new FileLogSettings
        {
            FilePath = filePath,
            FileOpenMode = FileOpenMode.Append,
            EnabledLogLevels = [VostokLogLevel.Info, VostokLogLevel.Warn, VostokLogLevel.Error, VostokLogLevel.Fatal],
            RollingStrategy = new RollingStrategyOptions
            {
                Type = RollingStrategyType.BySize,
                MaxSize = MaxFileSize,
                MaxFiles = MaxFiles
            }
        });
    }
}
