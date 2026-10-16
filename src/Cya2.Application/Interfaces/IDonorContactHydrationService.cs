using Cya2.Core.ReadModels;

namespace Cya2.Application.Interfaces;

public interface IDonorContactHydrationService
{
    Task HydrateAsync(IEnumerable<DonationRecord> donations, CancellationToken cancellationToken = default);
}
