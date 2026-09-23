using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;

namespace Cya2.Application.Services;

public sealed class AccountingTransactionProcessor : IAccountingTransactionProcessor
{
    private readonly ExpenseClassificationService _classificationService;

    public AccountingTransactionProcessor(ExpenseClassificationService classificationService)
    {
        _classificationService = classificationService;
    }

    public BalanceCalculationResult Process(
        UserAccountContextAccount account,
        IEnumerable<AccountingRecord> candidates,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var matched = (candidates ?? [])
            .Where(row => AccountingDataMatcher.MatchesAccount(
                row,
                account.AccountingClass,
                account.AccountNumber));

        return ProcessCandidates(matched, account.BalanceAdjustment, startDate, endDate);
    }

    public BalanceCalculationResult ProcessCandidates(
        IEnumerable<AccountingRecord> candidates,
        decimal balanceAdjustment = 0m,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var entries = (candidates ?? [])
            .Where(entry =>
                (!startDate.HasValue || entry.Date >= startDate.Value) &&
                (!endDate.HasValue || entry.Date <= endDate.Value))
            .Where(entry => !_classificationService.IsExcludedFromBalance(entry))
            .ToList();

        var categorized = _classificationService.Categorize(entries);
        var calculatedBalance = balanceAdjustment + entries.Sum(entry =>
            _classificationService.ShouldSubtractFromBalance(entry)
                ? -Convert.ToDecimal(entry.Amount)
                : Convert.ToDecimal(entry.Amount));

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
}
