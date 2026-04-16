using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface ISessionDonorSummaryCacheService
{
    bool TryGetDonorSummaries(string fundsSignature, DateTime startDate, DateTime endDate, out List<DonorSummaryDto> data);
    void SetDonorSummaries(string fundsSignature, DateTime startDate, DateTime endDate, List<DonorSummaryDto> data);
    void InvalidateAll();
}
