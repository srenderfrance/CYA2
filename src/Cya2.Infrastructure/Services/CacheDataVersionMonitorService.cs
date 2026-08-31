using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cya2.Infrastructure.Services;

public sealed class CacheDataVersionMonitorService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IImportCacheInvalidator _cacheInvalidator;
    private readonly ICacheDataVersionProvider _dataVersionProvider;
    private readonly ILogger<CacheDataVersionMonitorService> _logger;
    private string? _lastMarker;

    public CacheDataVersionMonitorService(
        IConfiguration configuration,
        IImportCacheInvalidator cacheInvalidator,
        ICacheDataVersionProvider dataVersionProvider,
        ILogger<CacheDataVersionMonitorService> logger)
    {
        _configuration = configuration;
        _cacheInvalidator = cacheInvalidator;
        _dataVersionProvider = dataVersionProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue("CacheInvalidation:EnableDataVersionMonitor", true);
        if (!enabled)
        {
            _logger.LogInformation("Cache data-version monitor is disabled by configuration");
            return;
        }

        var intervalMinutes = Math.Max(1, _configuration.GetValue("CacheInvalidation:MonitorIntervalMinutes", 15));
        var interval = TimeSpan.FromMinutes(intervalMinutes);

        _logger.LogInformation("Cache data-version monitor started (interval={IntervalMinutes}m)", intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var marker = await _dataVersionProvider.GetDataMarkerAsync(stoppingToken);
                if (!string.IsNullOrWhiteSpace(marker))
                {
                    if (_lastMarker is null)
                    {
                        _lastMarker = marker;
                    }
                    else if (!string.Equals(_lastMarker, marker, StringComparison.Ordinal))
                    {
                        _logger.LogInformation("Detected source data marker change. Invalidating all caches.");
                        _cacheInvalidator.InvalidateAll();
                        _lastMarker = marker;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache data-version monitor check failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
