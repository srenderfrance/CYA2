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

    public Task<decimal> GetInternDonationTotalAsync(string internDesignationName, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        var normalizedDesignation = internDesignationName?.Trim() ?? string.Empty;
        var alternateDesignation = InternAccountUtility.GetAlternateDesignationName(normalizedDesignation);
        var hasAlternateDesignation = !string.IsNullOrWhiteSpace(alternateDesignation);
        var hasNameTokens = InternAccountUtility.TryGetFirstAndLastName(normalizedDesignation, out var firstName, out var lastName);
        var designationLookupKey = InternAccountUtility.BuildLookupKey(normalizedDesignation);
        var alternateLookupKey = InternAccountUtility.BuildLookupKey(alternateDesignation);
        var hasAlternateLookupKey = !string.IsNullOrWhiteSpace(alternateLookupKey);
        const string sql = @"
SELECT COALESCE(SUM(Amount), 0)
FROM DonationData
WHERE Date >= @StartDate
  AND Date <= @EndDate
  AND (
      Intern COLLATE utf8mb4_0900_ai_ci = @InternDesignationName COLLATE utf8mb4_0900_ai_ci
      OR (@HasAlternateDesignation = 1 AND Intern COLLATE utf8mb4_0900_ai_ci = @AlternateDesignation COLLATE utf8mb4_0900_ai_ci)
      OR (
          LOCATE(',', Intern) > 0
          AND TRIM(CONCAT(
              TRIM(SUBSTRING_INDEX(Intern, ',', -1)),
              ' ',
              TRIM(SUBSTRING_INDEX(Intern, ',', 1))
          )) COLLATE utf8mb4_0900_ai_ci = @InternDesignationName COLLATE utf8mb4_0900_ai_ci
      )
      OR (
          @HasAlternateDesignation = 1
          AND LOCATE(',', Intern) > 0
          AND TRIM(CONCAT(
              TRIM(SUBSTRING_INDEX(Intern, ',', -1)),
              ' ',
              TRIM(SUBSTRING_INDEX(Intern, ',', 1))
          )) COLLATE utf8mb4_0900_ai_ci = @AlternateDesignation COLLATE utf8mb4_0900_ai_ci
      )
      OR (
          @HasNameTokens = 1
          AND Intern IS NOT NULL
          AND Intern COLLATE utf8mb4_0900_ai_ci LIKE @FirstToken COLLATE utf8mb4_0900_ai_ci
          AND Intern COLLATE utf8mb4_0900_ai_ci LIKE @LastToken COLLATE utf8mb4_0900_ai_ci
      )
      OR (
          LOWER(REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(Intern,''), ' ', ''), ',', ''), '.', ''), '-', '')) = @DesignationLookupKey
      )
      OR (
          @HasAlternateLookupKey = 1
          AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(Intern,''), ' ', ''), ',', ''), '.', ''), '-', '')) = @AlternateLookupKey
      )
  )";

        _logger.LogInformation(
            "Intern dashboard total query debug: designation='{Designation}', alternate='{Alternate}', hasAlternate={HasAlternate}, firstName='{FirstName}', lastName='{LastName}', hasNameTokens={HasNameTokens}, lookupKey='{LookupKey}', alternateLookupKey='{AlternateLookupKey}', range={StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}",
            normalizedDesignation,
            alternateDesignation,
            hasAlternateDesignation,
            firstName,
            lastName,
            hasNameTokens,
            designationLookupKey,
            alternateLookupKey,
            startDate,
            endDate);

        return QuerySingleDecimalAsync(sql, new
        {
            StartDate = startDate,
            EndDate = endDate,
            InternDesignationName = normalizedDesignation,
            AlternateDesignation = alternateDesignation,
            HasAlternateDesignation = hasAlternateDesignation ? 1 : 0,
            HasNameTokens = hasNameTokens ? 1 : 0,
            FirstToken = $"%{firstName}%",
            LastToken = $"%{lastName}%",
            DesignationLookupKey = designationLookupKey,
            AlternateLookupKey = alternateLookupKey,
            HasAlternateLookupKey = hasAlternateLookupKey ? 1 : 0
        });
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
        var value = await conn.ExecuteScalarAsync<decimal>(sql, parameters);
        _logger.LogDebug("Dashboard scalar query result: {Value}", value);
        return value;
    }
}
