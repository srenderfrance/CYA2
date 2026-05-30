using Cya2.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;

namespace cya2.Services;

public sealed class CacheDataVersionMonitorService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IImportCacheInvalidator _cacheInvalidator;
    private readonly ILogger<CacheDataVersionMonitorService> _logger;
    private string? _lastMarker;

    public CacheDataVersionMonitorService(
        IConfiguration configuration,
        IImportCacheInvalidator cacheInvalidator,
        ILogger<CacheDataVersionMonitorService> logger)
    {
        _configuration = configuration;
        _cacheInvalidator = cacheInvalidator;
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
                var marker = await BuildDataMarkerAsync(stoppingToken);
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

    private async Task<string> BuildDataMarkerAsync(CancellationToken cancellationToken)
    {
        var connStr = _configuration.GetConnectionString("default") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connStr))
        {
            return string.Empty;
        }

        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync(cancellationToken);

        var donationMarker = await ReadTableMarkerAsync(conn, "DonationData", cancellationToken);
        var accountingMarker = await ReadTableMarkerAsync(conn, "AccountingData", cancellationToken);

        return $"D:{donationMarker}|A:{accountingMarker}";
    }

    private static async Task<string> ReadTableMarkerAsync(MySqlConnection conn, string tableName, CancellationToken cancellationToken)
    {
        var commandText = $@"
SELECT
    COUNT(*) AS RowCount,
    COALESCE(MAX(DateCreated), '1900-01-01') AS MaxCreated,
    COALESCE(MAX(`Date`), '1900-01-01') AS MaxDate
FROM {tableName};";

        await using var cmd = new MySqlCommand(commandText, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return "0|1900-01-01|1900-01-01";
        }

        var rowCount = reader.GetInt64(0);
        var maxCreated = reader.GetDateTime(1);
        var maxDate = reader.GetDateTime(2);

        return $"{rowCount}|{maxCreated:O}|{maxDate:O}";
    }
}
