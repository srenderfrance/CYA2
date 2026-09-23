using Cya2.Core.Entities;
using Cya2.Core.ReadModels;

namespace Cya2.Application.Interfaces;

public interface IAccountingTransactionProcessor
{
    BalanceCalculationResult Process(
        UserAccountContextAccount account,
        IEnumerable<AccountingRecord> candidates,
        DateTime? startDate = null,
        DateTime? endDate = null);

    BalanceCalculationResult ProcessCandidates(
        IEnumerable<AccountingRecord> candidates,
        decimal balanceAdjustment = 0m,
        DateTime? startDate = null,
        DateTime? endDate = null);
}
