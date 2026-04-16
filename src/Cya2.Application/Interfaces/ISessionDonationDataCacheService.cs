using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces
{
    public interface ISessionDonationDataCacheService
    {
        bool TryGetDonationData(string userId, string fund, out DonationDataDto data);
        void SetDonationData(string userId, string fund, DonationDataDto data, bool prioritize = false);
        IReadOnlyCollection<string> GetFunds(string userId);
        void InvalidateAll();
    }
}