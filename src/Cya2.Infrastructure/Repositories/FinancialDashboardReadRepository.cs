using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.Utilities;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class FinancialDashboardReadRepository : IFinancialDashboardReadRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;
    private readonly ILogger<FinancialDashboardReadRepository> _logger;

    public FinancialDashboardReadRepository(IConfiguration configuration, IDatabaseGuard dbGuard, ILogger<FinancialDashboardReadRepository> logger)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
        _logger = logger;
    }

    private string ConnStr => _configuration.GetConnectionString("default") ?? string.Empty;

    public Task<decimal> GetTransferTotalAsync(Account account, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
SELECT COALESCE(SUM(ABS(Amount)), 0)
FROM AccountingData
WHERE (AccountingClass = @AccountingClass OR AccountNumber = @AccountNumber)
  AND Account != 'Prepaids'
  AND Date >= @StartDate
  AND Date <= @EndDate
  AND Account LIKE '%Transfer%'";
        return QuerySingleDecimalAsync(sql, new
        {
            AccountingClass = account.AccountingClass,
            AccountNumber = account.AccountNumber,
            StartDate = startDate,
            EndDate = endDate
        });
    }

    public async Task<decimal> GetBalanceAsOfAsync(Account account, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
SELECT COALESCE(SUM(
    CASE
        WHEN Type IN ('Payroll Check', 'Expense') OR Account LIKE '%Expenses:%' OR Account LIKE '%Payroll:%' OR Account LIKE '%Administration:%' THEN -Amount
        ELSE Amount
    END
), 0)
FROM AccountingData
WHERE (AccountingClass = @AccountingClass OR AccountNumber = @AccountNumber)
  AND Account != 'Prepaids'
  AND Date <= @EndDate";
        var baseBalance = await QuerySingleDecimalAsync(sql, new
        {
            AccountingClass = account.AccountingClass,
            AccountNumber = account.AccountNumber,
            EndDate = endDate
        });
        return baseBalance + account.BalanceAdjustment;
    }

    public async Task<IReadOnlyDictionary<int, decimal>> GetBalancesAsOfAsync(IReadOnlyList<Account> accounts, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        if (accounts.Count == 0)
        {
            return new Dictionary<int, decimal>();
        }

        var parameters = new DynamicParameters();
        parameters.Add("EndDate", endDate);
        parameters.Add("AccountingClasses", accounts.Select(a => a.AccountingClass).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        parameters.Add("AccountNumbers", accounts.Select(a => a.AccountNumber).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        var expressions = accounts.Select((account, index) =>
        {
            parameters.Add($"AccountingClass{index}", account.AccountingClass);
            parameters.Add($"AccountNumber{index}", account.AccountNumber);
            return $"COALESCE(SUM(CASE WHEN (AccountingClass = @AccountingClass{index} OR AccountNumber = @AccountNumber{index}) THEN CASE WHEN Type IN ('Payroll Check', 'Expense') OR Account LIKE '%Expenses:%' OR Account LIKE '%Payroll:%' OR Account LIKE '%Administration:%' THEN -Amount ELSE Amount END ELSE 0 END), 0) AS B{index}";
        });

        var sql = $@"
SELECT {string.Join(", ", expressions)}
FROM AccountingData
WHERE Account != 'Prepaids'
  AND Date <= @EndDate
  AND (AccountingClass IN @AccountingClasses OR AccountNumber IN @AccountNumbers)";

        await using var conn = new MySqlConnection(ConnStr);
        var row = await conn.QuerySingleAsync(sql, parameters);
        var values = (IDictionary<string, object>)row;
        var result = new Dictionary<int, decimal>(accounts.Count);
        for (var index = 0; index < accounts.Count; index++)
        {
            result[accounts[index].AccountId] = Convert.ToDecimal(values[$"B{index}"]) + accounts[index].BalanceAdjustment;
        }

        _logger.LogInformation("Dashboard account balances loaded in one query: accountCount={AccountCount}", accounts.Count);
        return result;
    }

    private async Task<decimal> QuerySingleDecimalAsync(string sql, object parameters)
    {
        await using var conn = new MySqlConnection(ConnStr);
        var value = await conn.ExecuteScalarAsync<decimal>(sql, parameters);
        _logger.LogDebug("Dashboard scalar query result: {Value}", value);
        return value;
    }
}
