using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IMissingGiftCache
{
    bool TryGet(
        int accountId,
        string fund,
        DateTime startDate,
        DateTime endDate,
        long donationsGeneration,
        long donorContactsGeneration,
        long authorizationGeneration,
        out List<DonorSummaryDto> data);

    void Set(
        int accountId,
        string fund,
        DateTime startDate,
        DateTime endDate,
        long donationsGeneration,
        long donorContactsGeneration,
        long authorizationGeneration,
        List<DonorSummaryDto> data);

    void InvalidateAll();
}
