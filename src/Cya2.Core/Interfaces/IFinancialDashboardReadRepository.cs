using Cya2.Core.Entities;

namespace Cya2.Core.Interfaces;

public interface IFinancialDashboardReadRepository
{
    Task<decimal> GetTransferTotalAsync(Account account, DateTime startDate, DateTime endDate);
    Task<decimal> GetBalanceAsOfAsync(Account account, DateTime endDate);
    Task<IReadOnlyDictionary<int, decimal>> GetBalancesAsOfAsync(IReadOnlyList<Account> accounts, DateTime endDate);
}
