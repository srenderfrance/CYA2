using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;

namespace Cya2.Application.Services;

public sealed class ReverseBalanceCalculationService : IReverseBalanceCalculationService
{
    private readonly ExpenseClassificationService _classificationService;

    public ReverseBalanceCalculationService(ExpenseClassificationService classificationService)
    {
        _classificationService = classificationService;
    }

    public BalanceCalculationResult CalculateFromEndOfDayAnchor(
        UserAccountContextAccount account,
        IEnumerable<AccountingRecord> transactions,
        DateTime anchorDate,
        decimal anchorBalance,
        DateTime targetDate)
    {
        ArgumentNullException.ThrowIfNull(account);

        var anchor = anchorDate.Date;
        var target = targetDate.Date;
        var lowerDate = anchor <= target ? anchor : target;
        var upperDate = anchor <= target ? target : anchor;
        var entries = (transactions ?? [])
            .Where(entry => AccountingDataMatcher.MatchesAccount(
                entry,
                account.AccountingClass,
                account.AccountNumber))
            .Where(entry => !_classificationService.IsExcludedFromBalance(entry))
            .Where(entry => entry.Date.Date > lowerDate && entry.Date.Date <= upperDate)
            .ToList();

        var signedTotal = entries.Sum(SignedAmount);
        var calculatedBalance = target >= anchor
            ? anchorBalance + signedTotal
            : anchorBalance - signedTotal;

        var categorized = _classificationService.Categorize(entries);
        return new BalanceCalculationResult
        {
            TotalBalance = calculatedBalance,
            ExpenseTotal = categorized.ExpenseTotal,
            TransferTotal = categorized.TransferTotal,
            OtherTotal = categorized.OtherTotal,
            ExpenseTransactions = categorized.ExpenseTransactions,
            TransferTransactions = categorized.TransferTransactions,
            OtherTransactions = categorized.OtherTransactions,
            AllTransactions = entries
        };
    }

    private decimal SignedAmount(AccountingRecord entry)
        => _classificationService.ShouldSubtractFromBalance(entry)
            ? -Convert.ToDecimal(entry.Amount)
            : Convert.ToDecimal(entry.Amount);
}
