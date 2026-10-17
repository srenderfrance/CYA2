using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class AdminCacheWarmupCoordinatorTests
{
    [Fact]
    public async Task QueueWarmup_DuringActiveRebuild_RunsOneFollowUpWithRequestedSummaryWarming()
    {
        var rebuild = new BlockingCacheRebuildService();
        using var coordinator = new AdminCacheWarmupCoordinator(
            new RebuildScopeFactory(rebuild),
            NullLogger<AdminCacheWarmupCoordinator>.Instance);

        coordinator.QueueWarmup(warmSummaries: false);
        await rebuild.FirstRebuildStarted.Task;

        coordinator.QueueWarmup(warmSummaries: false);
        coordinator.QueueWarmup(warmSummaries: true);
        coordinator.QueueWarmup(warmSummaries: false);

        rebuild.ReleaseFirstRebuild();
        await coordinator.WaitForWarmupAsync();

        Assert.Equal([false, true], rebuild.WarmSummaries);
    }

    private sealed class BlockingCacheRebuildService : ICacheRebuildService
    {
        private readonly TaskCompletionSource _firstRebuildStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstRebuild = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<bool> _warmSummaries = [];
        private readonly object _sync = new();

        public TaskCompletionSource FirstRebuildStarted => _firstRebuildStarted;
        public IReadOnlyList<bool> WarmSummaries
        {
            get
            {
                lock (_sync)
                {
                    return _warmSummaries.ToArray();
                }
            }
        }

        public async Task RebuildAsync(bool warmSummaries = true, CancellationToken cancellationToken = default)
        {
            int rebuildNumber;
            lock (_sync)
            {
                _warmSummaries.Add(warmSummaries);
                rebuildNumber = _warmSummaries.Count;
            }

            if (rebuildNumber == 1)
            {
                _firstRebuildStarted.SetResult();
                await _releaseFirstRebuild.Task.WaitAsync(cancellationToken);
            }
        }

        public void ReleaseFirstRebuild() => _releaseFirstRebuild.SetResult();
    }

    private sealed class RebuildScopeFactory(ICacheRebuildService rebuildService) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new RebuildScope(rebuildService);
    }

    private sealed class RebuildScope(ICacheRebuildService rebuildService) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new RebuildServiceProvider(rebuildService);
        public void Dispose()
        {
        }
    }

    private sealed class RebuildServiceProvider(ICacheRebuildService rebuildService) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(ICacheRebuildService) ? rebuildService : null;
    }
}
