using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface ISessionMissingGiftCacheService
{
    bool TryGetMissingGiftDonors(int accountId, string fund, DateTime startDate, DateTime endDate, out List<DonorSummaryDto> data);
    void SetMissingGiftDonors(int accountId, string fund, DateTime startDate, DateTime endDate, List<DonorSummaryDto> data);
    void InvalidateAll();
}
