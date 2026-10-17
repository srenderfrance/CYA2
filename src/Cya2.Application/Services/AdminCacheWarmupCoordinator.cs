using Cya2.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class AdminCacheWarmupCoordinator : IAdminCacheWarmupCoordinator, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminCacheWarmupCoordinator> _logger;
    private readonly object _sync = new();
    private Task? _warmupTask;
    private bool _followUpRequested;
    private bool _followUpWarmSummaries;
    private bool _disposed;

    public AdminCacheWarmupCoordinator(
        IServiceScopeFactory scopeFactory,
        ILogger<AdminCacheWarmupCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void QueueWarmup(bool warmSummaries = true)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                _logger.LogWarning("Central cache warmup request ignored because the coordinator is disposed");
                return;
            }

            if (_warmupTask is { IsCompleted: false })
            {
                _followUpRequested = true;
                _followUpWarmSummaries |= warmSummaries;
                _logger.LogInformation(
                    "Central cache warmup follow-up requested because a rebuild is already running: summaryWarming={SummaryWarming}",
                    _followUpWarmSummaries);
                return;
            }

            StartWarmupLocked(warmSummaries);
        }
    }

    public async Task WaitForWarmupAsync(CancellationToken cancellationToken = default)
    {
        Task? warmupTask;
        lock (_sync)
        {
            warmupTask = _warmupTask;
        }

        if (warmupTask is null)
        {
            return;
        }

        while (warmupTask is not null)
        {
            await warmupTask.WaitAsync(cancellationToken);
            lock (_sync)
            {
                warmupTask = !ReferenceEquals(_warmupTask, warmupTask) && _warmupTask is { IsCompleted: false }
                    ? _warmupTask
                    : null;
            }
        }
    }

    private void StartWarmupLocked(bool warmSummaries)
    {
        _logger.LogInformation("Central cache warmup queued: summaryWarming={SummaryWarming}", warmSummaries);
        _warmupTask = Task.Run(() => WarmupAsync(warmSummaries));
    }

    private async Task WarmupAsync(bool warmSummaries)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var cacheRebuildService = scope.ServiceProvider.GetRequiredService<ICacheRebuildService>();
            await cacheRebuildService.RebuildAsync(warmSummaries);
            _logger.LogInformation(
                "CACHE CREATION COMPLETED: metadata, bulk financial snapshot, Admin Overview, account balances, and {SummaryCacheStatus} are fully populated",
                warmSummaries ? "Home financial summary caches" : "no Home financial summary caches");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CACHE CREATION FAILED: central cache rebuild did not complete");
        }
        finally
        {
            lock (_sync)
            {
                if (!_disposed && _followUpRequested)
                {
                    var followUpWarmSummaries = _followUpWarmSummaries;
                    _followUpRequested = false;
                    _followUpWarmSummaries = false;
                    StartWarmupLocked(followUpWarmSummaries);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _followUpRequested = false;
            _followUpWarmSummaries = false;
        }
    }
}
