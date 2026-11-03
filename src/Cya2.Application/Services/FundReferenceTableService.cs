using Cya2.Application.DTOs;
using Cya2.Core.Entities;

namespace Cya2.Application.Services;

public static class FundReferenceTableService
{
    public static IReadOnlyList<FundReferenceDto> BuildRows(
        IReadOnlyList<Account> accounts,
        IReadOnlyList<SubAccount> subAccounts)
    {
        var parents = accounts.ToDictionary(account => account.AccountId);
        var rows = accounts.Select(account => CreateRow(account, account.Fund)).ToList();
        foreach (var subAccount in subAccounts)
        {
            if (parents.TryGetValue(subAccount.AccountId, out var parent))
            {
                rows.Add(CreateRow(parent, subAccount.SubFund));
            }
        }

        return rows.OrderBy(row => row.Fund, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static FundReferenceDto CreateRow(Account parent, string fund) => new()
    {
        AccountId = parent.AccountId,
        Fund = fund,
        AccountingClass = parent.AccountingClass ?? string.Empty,
        AccountNumber = parent.AccountNumber,
        Overhead = parent.Overhead
    };
}
