using Cya2.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace cya2.Services;

public sealed class AuditRetentionCleanupService : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditRetentionCleanupService> _logger;

    public AuditRetentionCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuditRetentionCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupAsync(stoppingToken);

        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupAsync(stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAuditEventRepository>();
            var deleted = await repository.DeleteExpiredAsync(DateTime.UtcNow.Subtract(Retention), cancellationToken);
            if (deleted > 0)
            {
                _logger.LogInformation("Audit retention cleanup removed {AuditEventCount} records.", deleted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit retention cleanup failed.");
        }
    }
}
