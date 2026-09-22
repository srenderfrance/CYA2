using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class ExpenseReadRepository : IExpenseReadRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;

    public ExpenseReadRepository(IConfiguration configuration, IDatabaseGuard dbGuard)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
    }

    private string ConnStr => _configuration.GetConnectionString("default") ?? string.Empty;

    public async Task<List<AccountingRecord>> GetAccountingDataForAccountAsync(
        string accountingClass,
        string accountNumber,
        DateTime startDate,
        DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated
FROM AccountingData
 WHERE ((@AccountNumber = '2200000' AND AccountingClass = @AccountClass)
        OR (@AccountNumber <> '2200000' AND (AccountingClass = @AccountClass OR AccountNumber = @AccountNumber)))
  AND LOWER(COALESCE(Account, '')) <> 'prepaids'
  AND LOWER(COALESCE(Account, '')) <> 'payroll clearing insurance'
  AND Date >= @StartDate
  AND Date <= @EndDate
ORDER BY Date";
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<AccountingRecord>(sql, new { AccountClass = accountingClass, AccountNumber = accountNumber, StartDate = startDate, EndDate = endDate });
        return rows.ToList();
    }

    public async Task<IReadOnlyDictionary<int, List<AccountingRecord>>> GetAccountingDataForAccountsAsync(
        IReadOnlyList<(int AccountId, string AccountingClass, string AccountNumber)> accounts,
        DateTime startDate,
        DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        if (accounts.Count == 0)
        {
            return new Dictionary<int, List<AccountingRecord>>();
        }

        var parameters = new DynamicParameters();
        parameters.Add("StartDate", startDate);
        parameters.Add("EndDate", endDate);
        var predicates = accounts.Select((account, index) =>
        {
            parameters.Add($"AccountingClass{index}", account.AccountingClass);
            parameters.Add($"AccountNumber{index}", account.AccountNumber);
            return $"((@AccountNumber{index} = '2200000' AND AccountingClass = @AccountingClass{index}) " +
                   $"OR (@AccountNumber{index} <> '2200000' AND (AccountingClass = @AccountingClass{index} OR AccountNumber = @AccountNumber{index})))";
        });

        var sql = $@"
SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated
FROM AccountingData
WHERE ({string.Join(" OR ", predicates)})
  AND LOWER(COALESCE(Account, '')) <> 'prepaids'
  AND LOWER(COALESCE(Account, '')) <> 'payroll clearing insurance'
  AND Date >= @StartDate
  AND Date <= @EndDate
ORDER BY Date";

        await using var conn = new MySqlConnection(ConnStr);
        var rows = (await conn.QueryAsync<AccountingRecord>(sql, parameters)).ToList();
        var result = accounts.ToDictionary(account => account.AccountId, _ => new List<AccountingRecord>());
        foreach (var row in rows)
        {
            foreach (var account in accounts)
            {
                if (AccountingDataMatcher.MatchesAccount(row, account.AccountingClass, account.AccountNumber))
                {
                    result[account.AccountId].Add(row);
                }
            }
        }

        return result;
    }

}
