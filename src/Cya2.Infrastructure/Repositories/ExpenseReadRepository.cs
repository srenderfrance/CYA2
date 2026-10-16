using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System.Diagnostics;

namespace Cya2.Infrastructure.Repositories;

public sealed class ExpenseReadRepository : IExpenseReadRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;
    private readonly ILogger<ExpenseReadRepository> _logger;

    public ExpenseReadRepository(
        IConfiguration configuration,
        IDatabaseGuard dbGuard,
        ILogger<ExpenseReadRepository> logger)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
        _logger = logger;
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
 WHERE (AccountingClass = @AccountClass OR AccountNumber = @AccountNumber)
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
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Accounting batch load entered: accountCount={AccountCount}",
            accounts.Count);

        if (accounts.Count == 0)
        {
            return new Dictionary<int, List<AccountingRecord>>();
        }

        try
        {
            var accountingClasses = accounts
                .Select(account => account.AccountingClass?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var accountNumbers = accounts
                .Select(account => account.AccountNumber?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var parameters = new DynamicParameters();
            parameters.Add("StartDate", startDate);
            parameters.Add("EndDate", endDate);
            parameters.Add("AccountingClasses", accountingClasses);
            parameters.Add("AccountNumbers", accountNumbers);
            var matchPredicates = new List<string>(2);
            if (accountingClasses.Length > 0)
            {
                matchPredicates.Add("AccountingClass IN @AccountingClasses");
            }

            if (accountNumbers.Length > 0)
            {
                matchPredicates.Add("AccountNumber IN @AccountNumbers");
            }

            if (matchPredicates.Count == 0)
            {
                return accounts.ToDictionary(account => account.AccountId, _ => new List<AccountingRecord>());
            }

            var sql = $@"
SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated
FROM AccountingData
WHERE ({string.Join(" OR ", matchPredicates)})
  AND Date >= @StartDate
  AND Date <= @EndDate
ORDER BY Date";

            _logger.LogInformation(
                "Accounting batch query starting: accountCount={AccountCount}, classCount={ClassCount}, accountNumberCount={AccountNumberCount}",
                accounts.Count,
                accountingClasses.Length,
                accountNumbers.Length);
            await using var conn = new MySqlConnection(ConnStr);
            var rows = (await conn.QueryAsync<AccountingRecord>(
                new CommandDefinition(sql, parameters, commandTimeout: 120))).ToList();
            _logger.LogInformation(
                "Accounting batch query returned: accountCount={AccountCount}, rowCount={RowCount}, elapsedMs={ElapsedMs}",
                accounts.Count,
                rows.Count,
                stopwatch.ElapsedMilliseconds);

            var result = accounts.ToDictionary(account => account.AccountId, _ => new List<AccountingRecord>());
            var accountIdsByClass = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.AccountingClass))
            .GroupBy(account => account.AccountingClass, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(account => account.AccountId).ToArray(),
                StringComparer.OrdinalIgnoreCase);
            var accountIdsByNumber = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.AccountNumber))
            .GroupBy(account => account.AccountNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(account => account.AccountId).ToArray(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var accountIds = new HashSet<int>();
                if (accountIdsByClass.TryGetValue(row.AccountingClass ?? string.Empty, out var classAccountIds))
                {
                    accountIds.UnionWith(classAccountIds);
                }

                if (accountIdsByNumber.TryGetValue(row.AccountNumber ?? string.Empty, out var numberAccountIds))
                {
                    accountIds.UnionWith(numberAccountIds);
                }

                foreach (var accountId in accountIds)
                {
                    result[accountId].Add(row);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Accounting batch load failed: accountCount={AccountCount}, elapsedMs={ElapsedMs}",
                accounts.Count,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

}
