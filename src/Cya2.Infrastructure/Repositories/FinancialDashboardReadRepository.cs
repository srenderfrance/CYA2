using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class FinancialDashboardReadRepository : IFinancialDashboardReadRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;

    public FinancialDashboardReadRepository(IConfiguration configuration, IDatabaseGuard dbGuard)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
    }

    private string ConnStr => _configuration.GetConnectionString("default") ?? string.Empty;

    public Task<decimal> GetDonationTotalAsync(Account account, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
SELECT COALESCE(SUM(Amount), 0)
FROM DonationData
WHERE Date >= @StartDate
  AND Date <= @EndDate
  AND (
        Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
        OR Fund COLLATE utf8mb4_0900_ai_ci IN (
            SELECT SubFund COLLATE utf8mb4_0900_ai_ci
            FROM SubAccounts
            WHERE AccountId = @AccountId AND Kind = 'Merged'
        )
  )";
        return QuerySingleDecimalAsync(sql, new { StartDate = startDate, EndDate = endDate, account.Fund, account.AccountId });
    }

    public Task<decimal> GetExpenseTotalAsync(Account account, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
SELECT COALESCE(SUM(ABS(Amount)), 0)
FROM AccountingData
WHERE (AccountingClass = @AccountingClass OR AccountNumber = @AccountNumber)
  AND Account != 'Prepaids'
  AND Date >= @StartDate
  AND Date <= @EndDate
  AND (
        Type IN ('Payroll Check', 'Expense')
        OR Account LIKE '%Expenses:%'
        OR Account LIKE '%Payroll:%'
        OR Account LIKE '%Administration:%'
  )";
        return QuerySingleDecimalAsync(sql, new
        {
            AccountingClass = account.AccountingClass,
            AccountNumber = account.AccountNumber,
            StartDate = startDate,
            EndDate = endDate
        });
    }

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

    private async Task<decimal> QuerySingleDecimalAsync(string sql, object parameters)
    {
        await using var conn = new MySqlConnection(ConnStr);
        return await conn.ExecuteScalarAsync<decimal>(sql, parameters);
    }
}
