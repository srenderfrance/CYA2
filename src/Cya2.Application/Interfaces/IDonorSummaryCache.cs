using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IDonorSummaryCache
{
    bool TryGet(
        string scope,
        DateTime startDate,
        DateTime endDate,
        long donationsGeneration,
        long donorContactsGeneration,
        long authorizationGeneration,
        out List<DonorSummaryDto> data);

    void Set(
        string scope,
        DateTime startDate,
        DateTime endDate,
        long donationsGeneration,
        long donorContactsGeneration,
        long authorizationGeneration,
        List<DonorSummaryDto> data);

    void InvalidateAll();
}
