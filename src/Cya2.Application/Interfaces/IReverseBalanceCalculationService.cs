using Cya2.Core.Entities;
using Cya2.Core.ReadModels;

namespace Cya2.Application.Interfaces;

public interface IReverseBalanceCalculationService
{
    BalanceCalculationResult CalculateFromEndOfDayAnchor(
        UserAccountContextAccount account,
        IEnumerable<AccountingRecord> transactions,
        DateTime anchorDate,
        decimal anchorBalance,
        DateTime targetDate);
}
