using Cya2.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace cya2.Services;

internal sealed class DatabaseStartupInitializer
{
    public static bool Initialize(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<DatabaseStartupInitializer>>();
        var monitor = services.GetRequiredService<IDatabaseAvailabilityMonitor>();
        var startupProbe = services.GetRequiredService<IDatabaseStartupProbe>();
        var initialDbConnected = false;

        try
        {
            logger.LogDebug("Lightweight DB check...");
            logger.LogDebug("Testing database connectivity before enabling MySQL operations");
            initialDbConnected = startupProbe.Check();
            logger.LogDebug("Database connection test result: {Result}", initialDbConnected);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Database connectivity check failed");
        }

        GlobalSettings.AllowMySqlLoading = initialDbConnected;
        if (!initialDbConnected)
        {
            logger.LogDebug("Database unavailable - limited mode");
            monitor.MarkAsDisconnected("Startup connectivity check failed");
        }

        monitor.Resume();
        return initialDbConnected;
    }
}
