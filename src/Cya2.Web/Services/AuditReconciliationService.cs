using Cya2.Core.Entities;
using Cya2.Core.Interfaces;

namespace cya2.Services;

public sealed class AuditReconciliationService(IServiceScopeFactory scopeFactory, ILogger<AuditReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            await ReconcileAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAuditEventRepository>();
            var count = await repository.FlagInterruptedImportsAsync(AuditOperationScope.HostInstanceId, DateTime.UtcNow.AddMinutes(-5), cancellationToken);
            if (count > 0)
                logger.LogWarning(new EventId(9102, "AuditReconciliationRequired"), "Unfinished imports require reconciliation. OperationCount={OperationCount}", count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(new EventId(9101, "AuditReconciliationUnavailable"), "Audit reconciliation is unavailable; it will retry. ExceptionType={ExceptionType}", ex.GetType().Name);
        }
    }
}
