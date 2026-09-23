using Cya2.Core.ReadModels;

namespace Cya2.Core.Services;

public static class AccountingDataMatcher
{
    public static bool MatchesAccount(
        AccountingRecord row,
        string accountingClass,
        string accountNumber)
    {
        var classMatches = string.Equals(row.AccountingClass, accountingClass, StringComparison.OrdinalIgnoreCase);
        return string.Equals(accountNumber, "2200000", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(accountNumber, "FADH", StringComparison.OrdinalIgnoreCase)
            ? classMatches
            : classMatches || string.Equals(row.AccountNumber, accountNumber, StringComparison.OrdinalIgnoreCase);
    }
}
