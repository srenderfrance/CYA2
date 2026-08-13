using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Utilities;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class DonationReadRepository : IDonationReadRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;
    private readonly ILogger<DonationReadRepository> _logger;

    public DonationReadRepository(IConfiguration configuration, IDatabaseGuard dbGuard, ILogger<DonationReadRepository> logger)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
        _logger = logger;
    }

    private string ConnStr => _configuration.GetConnectionString("default") ?? string.Empty;

    public async Task<List<SubAccount>> GetSubAccountsByAccountIdAsync(int accountId)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<SubAccount>(
            "SELECT Id, AccountId, SubFund, Kind FROM SubAccounts WHERE AccountId = @AccountId",
            new { AccountId = accountId });
        return rows.ToList();
    }

    public Task<List<DonationRecord>> GetDonationsByFundsAsync(IEnumerable<string> fundNames)
    {
        _dbGuard.ThrowIfUnavailable();
        return LoadByFundsAsync(fundNames,
            @"SELECT *
FROM DonationData
WHERE Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
   OR Fund COLLATE utf8mb4_0900_ai_ci IN (
        SELECT SubFund COLLATE utf8mb4_0900_ai_ci
        FROM SubAccounts
        WHERE AccountId = @AccountId AND Kind = 'Merged'
   )",
            (fund, accountId) => new { Fund = fund, AccountId = accountId });
    }

    public async Task<List<DonationRecord>> GetDonationsByAccountAsync(int accountId, string fundName)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<DonationRecord>(
            @"SELECT *
FROM DonationData
WHERE Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
   OR Fund COLLATE utf8mb4_0900_ai_ci IN (
        SELECT SubFund COLLATE utf8mb4_0900_ai_ci
        FROM SubAccounts
        WHERE AccountId = @AccountId AND Kind = 'Merged'
   )",
            new { AccountId = accountId, Fund = fundName });
        return rows.ToList();
    }

    public Task<List<DonationRecord>> GetDonationsByFundsAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        return LoadByFundsAsync(fundNames,
            @"SELECT *
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
  )",
            (fund, accountId) => new { Fund = fund, AccountId = accountId, StartDate = startDate, EndDate = endDate });
    }

    public async Task<List<DonationRecord>> GetDonationsByAccountAndDateRangeAsync(int accountId, string fundName, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<DonationRecord>(
            @"SELECT *
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
  )",
            new { AccountId = accountId, Fund = fundName, StartDate = startDate, EndDate = endDate });
        return rows.ToList();
    }

    public Task<List<DonationRecord>> GetDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string donorName)
    {
        _dbGuard.ThrowIfUnavailable();
        return LoadByFundsAsync(fundNames,
            @"SELECT *
FROM DonationData
WHERE AccountName = @DonorName
  AND (
       Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
       OR Fund COLLATE utf8mb4_0900_ai_ci IN (
            SELECT SubFund COLLATE utf8mb4_0900_ai_ci
            FROM SubAccounts
            WHERE AccountId = @AccountId AND Kind = 'Merged'
       )
  )",
            (fund, accountId) => new { Fund = fund, AccountId = accountId, DonorName = donorName });
    }

    public async Task<List<DonationRecord>> GetDonationsByAccountAndDonorAsync(int accountId, string fundName, string donorName)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<DonationRecord>(
            @"SELECT *
FROM DonationData
WHERE AccountName = @DonorName
  AND (
       Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
       OR Fund COLLATE utf8mb4_0900_ai_ci IN (
            SELECT SubFund COLLATE utf8mb4_0900_ai_ci
            FROM SubAccounts
            WHERE AccountId = @AccountId AND Kind = 'Merged'
       )
  )",
            new { AccountId = accountId, Fund = fundName, DonorName = donorName });
        return rows.ToList();
    }

    public Task<List<DonationRecord>> SearchDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string searchTerm)
    {
        _dbGuard.ThrowIfUnavailable();
        var likeTerm = $"%{searchTerm}%";
        return LoadByFundsAsync(fundNames,
            @"SELECT *
FROM DonationData
WHERE AccountName LIKE @SearchTerm
  AND (
       Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
       OR Fund COLLATE utf8mb4_0900_ai_ci IN (
            SELECT SubFund COLLATE utf8mb4_0900_ai_ci
            FROM SubAccounts
            WHERE AccountId = @AccountId AND Kind = 'Merged'
       )
  )",
            (fund, accountId) => new { Fund = fund, AccountId = accountId, SearchTerm = likeTerm });
    }

    public async Task<List<DonationRecord>> SearchDonationsByAccountAndDonorAsync(int accountId, string fundName, string searchTerm)
    {
        _dbGuard.ThrowIfUnavailable();
        var likeTerm = $"%{searchTerm}%";
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<DonationRecord>(
            @"SELECT *
FROM DonationData
WHERE AccountName LIKE @SearchTerm
  AND (
       Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
       OR Fund COLLATE utf8mb4_0900_ai_ci IN (
            SELECT SubFund COLLATE utf8mb4_0900_ai_ci
            FROM SubAccounts
            WHERE AccountId = @AccountId AND Kind = 'Merged'
       )
  )",
            new { AccountId = accountId, Fund = fundName, SearchTerm = likeTerm });
        return rows.ToList();
    }

    public async Task<List<DonationRecord>> GetInternDonationsByDesignationAndDateRangeAsync(string internDesignationName, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();

        if (string.IsNullOrWhiteSpace(internDesignationName))
        {
            return new List<DonationRecord>();
        }

        var normalizedDesignation = internDesignationName.Trim();
        var alternateDesignation = InternAccountUtility.GetAlternateDesignationName(normalizedDesignation);
        var hasAlternateDesignation = !string.IsNullOrWhiteSpace(alternateDesignation);
        var hasNameTokens = InternAccountUtility.TryGetFirstAndLastName(normalizedDesignation, out var firstName, out var lastName);
        var designationLookupKey = InternAccountUtility.BuildLookupKey(normalizedDesignation);
        var alternateLookupKey = InternAccountUtility.BuildLookupKey(alternateDesignation);
        var hasAlternateLookupKey = !string.IsNullOrWhiteSpace(alternateLookupKey);

        await using var conn = new MySqlConnection(ConnStr);

        var exactMatchCount = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*)
FROM DonationData
WHERE Date >= @StartDate
  AND Date <= @EndDate
  AND Intern COLLATE utf8mb4_0900_ai_ci = @InternDesignationName COLLATE utf8mb4_0900_ai_ci",
            new
            {
                StartDate = startDate,
                EndDate = endDate,
                InternDesignationName = normalizedDesignation
            });

        var alternateMatchCount = hasAlternateDesignation
            ? await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
FROM DonationData
WHERE Date >= @StartDate
  AND Date <= @EndDate
  AND Intern COLLATE utf8mb4_0900_ai_ci = @AlternateDesignation COLLATE utf8mb4_0900_ai_ci",
                new
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    AlternateDesignation = alternateDesignation
                })
            : 0;

        var tokenMatchCount = hasNameTokens
            ? await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
FROM DonationData
WHERE Date >= @StartDate
  AND Date <= @EndDate
  AND Intern IS NOT NULL
  AND (
      Intern COLLATE utf8mb4_0900_ai_ci LIKE @FirstToken COLLATE utf8mb4_0900_ai_ci
      OR Intern COLLATE utf8mb4_0900_ai_ci LIKE @LastToken COLLATE utf8mb4_0900_ai_ci
  )",
                new
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    FirstToken = $"%{firstName}%",
                    LastToken = $"%{lastName}%"
                })
            : 0;

        var normalizedMatchCount = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*)
FROM DonationData
WHERE Date >= @StartDate
  AND Date <= @EndDate
  AND (
      LOWER(REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(Intern,''), ' ', ''), ',', ''), '.', ''), '-', '')) = @DesignationLookupKey
      OR (@HasAlternateLookupKey = 1 AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(Intern,''), ' ', ''), ',', ''), '.', ''), '-', '')) = @AlternateLookupKey)
  )",
            new
            {
                StartDate = startDate,
                EndDate = endDate,
                DesignationLookupKey = designationLookupKey,
                AlternateLookupKey = alternateLookupKey,
                HasAlternateLookupKey = hasAlternateLookupKey ? 1 : 0
            });

        _logger.LogInformation(
            "Intern donation query debug: designation='{Designation}', alternate='{Alternate}', hasAlternate={HasAlternate}, firstName='{FirstName}', lastName='{LastName}', hasNameTokens={HasNameTokens}, lookupKey='{LookupKey}', alternateLookupKey='{AlternateLookupKey}', exactMatches={ExactMatches}, alternateMatches={AlternateMatches}, tokenMatches={TokenMatches}, normalizedMatches={NormalizedMatches}, range={StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}",
            normalizedDesignation,
            alternateDesignation,
            hasAlternateDesignation,
            firstName,
            lastName,
            hasNameTokens,
            designationLookupKey,
            alternateLookupKey,
            exactMatchCount,
            alternateMatchCount,
            tokenMatchCount,
            normalizedMatchCount,
            startDate,
            endDate);

        var rows = await conn.QueryAsync<DonationRecord>(
            @"SELECT
    Id,
    Date,
    Frequency,
    AccountName,
    PaymentMethod,
    GiftType,
    Amount,
    Fund,
    Intern,
    Addressee,
    SoftCreditName,
    Address,
    City,
    State,
    PostalCode,
    Country,
    Email,
    PhoneFixed,
    PhoneMobile,
    DateCreated,
    IsAnonymous
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
  )",
            new
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

        var rowList = rows.ToList();
        if (rowList.Count == 0)
        {
            var topInternValues = await conn.QueryAsync<string>(
                @"SELECT COALESCE(Intern, '')
FROM DonationData
WHERE Date >= @StartDate
  AND Date <= @EndDate
  AND Intern IS NOT NULL
  AND Intern <> ''
GROUP BY Intern
ORDER BY COUNT(*) DESC
LIMIT 5",
                new
                {
                    StartDate = startDate,
                    EndDate = endDate
                });

            _logger.LogInformation(
                "Intern donation query sample values in range: {InternSamples}",
                string.Join(" | ", topInternValues));
        }

        _logger.LogInformation(
            "Intern donation query returned {RowCount} rows for designation='{Designation}' (alternate='{Alternate}')",
            rowList.Count,
            normalizedDesignation,
            alternateDesignation);

        return rowList;
    }

    private async Task<List<DonationRecord>> LoadByFundsAsync(
        IEnumerable<string> fundNames,
        string sql,
        Func<string, int?, object> parameterFactory)
    {
        var funds = fundNames
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (funds.Count == 0) return new List<DonationRecord>();

        await using var conn = new MySqlConnection(ConnStr);
        var results = new List<DonationRecord>();
        foreach (var fund in funds)
        {
            var accountId = await conn.ExecuteScalarAsync<int?>(
                "SELECT AccountId FROM Accounts WHERE Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci LIMIT 1",
                new { Fund = fund });

            var rows = await conn.QueryAsync<DonationRecord>(sql, parameterFactory(fund, accountId));
            results.AddRange(rows);
        }

        return results;
    }
}
