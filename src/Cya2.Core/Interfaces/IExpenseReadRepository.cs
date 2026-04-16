using Cya2.Core.ReadModels;

namespace Cya2.Core.Interfaces;

public interface IExpenseReadRepository
{
    Task<List<AccountingRecord>> GetAccountingDataByClassAndDateAsync(string accountingClass, DateTime startDate, DateTime endDate);
    Task<List<AccountingRecord>> GetAccountingDataByClassOrAccountNumberAndDateAsync(string accountingClass, string accountNumber, DateTime startDate, DateTime endDate);
}
