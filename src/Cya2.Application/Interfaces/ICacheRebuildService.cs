namespace Cya2.Application.Interfaces;

public interface ICacheRebuildService
{
    Task RebuildAsync(bool warmSummaries = true, CancellationToken cancellationToken = default);
}
