using Cya2.Application.Models;
using Cya2.Core.Entities;
using Cya2.Core.ReadModels;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Interfaces;

public interface IAccountFactDataService
{
    Task<DonationCalculationData> GetDonationDataAsync(UserAccountContextAccount account, string userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
    Task<List<AccountingRecord>> GetAccountingDataAsync(UserAccountContextAccount account, string userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
}
