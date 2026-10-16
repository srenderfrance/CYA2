namespace Cya2.Application.Interfaces;

public interface IAdminCacheWarmupCoordinator
{
    void QueueWarmup(bool warmSummaries = true);
    Task WaitForWarmupAsync(CancellationToken cancellationToken = default);
}
