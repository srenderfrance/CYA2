using Cya2.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace cya2.Services.Imports;

public sealed class ImportRetentionCleanupService : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
    private readonly ImportPreviewStore _previews;
    private readonly ImportProgressService _progress;
    private readonly ILogger<ImportRetentionCleanupService> _logger;

    public ImportRetentionCleanupService(
        ImportPreviewStore previews,
        ImportProgressService progress,
        ILogger<ImportRetentionCleanupService> logger)
    {
        _previews = previews;
        _progress = progress;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            var previews = _previews.RemoveExpired(now, Retention);
            var progress = _progress.RemoveExpired(now, Retention);
            if (previews > 0 || progress > 0)
            {
                _logger.LogInformation("Import retention cleanup removed {PreviewCount} previews and {ProgressCount} progress records.", previews, progress);
            }
        }
    }
}
