using Cya2.Core.ReadModels;

namespace Cya2.Core.Interfaces;

public interface IDonorContactReadRepository
{
    Task<List<DonorContactRecord>> GetByDonorIdsAsync(
        IEnumerable<long> donorIds,
        CancellationToken cancellationToken = default);
}
