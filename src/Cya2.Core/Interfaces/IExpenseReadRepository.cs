using Cya2.Core.ReadModels;

namespace Cya2.Core.Interfaces;

public interface IExpenseReadRepository
{
    Task<List<AccountingRecord>> GetAccountingDataForAccountAsync(
        string accountingClass,
        string accountNumber,
        DateTime startDate,
        DateTime endDate);

    Task<IReadOnlyDictionary<int, List<AccountingRecord>>> GetAccountingDataForAccountsAsync(
        IReadOnlyList<(int AccountId, string AccountingClass, string AccountNumber)> accounts,
        DateTime startDate,
        DateTime endDate);
}
