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
    private bool _disposed;

    public AdminCacheWarmupCoordinator(
        IServiceScopeFactory scopeFactory,
        ILogger<AdminCacheWarmupCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void QueueWarmup()
    {
        lock (_sync)
        {
            if (_disposed || (_warmupTask is { IsCompleted: false }))
            {
                return;
            }

            _warmupTask = Task.Run(WarmupAsync);
        }
    }

    private async Task WarmupAsync()
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var preloadService = scope.ServiceProvider.GetRequiredService<IAdminPreloadService>();
            await preloadService.PreloadAsync(string.Empty);
            _logger.LogInformation("Admin shared cache warmup completed after cache invalidation");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Admin shared cache warmup failed; the next authorized Admin request will retry");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }
    }
}
