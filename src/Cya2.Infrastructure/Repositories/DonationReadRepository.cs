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

    public async Task<List<string>> GetCurrentInternNamesAsync()
    {
        if (!_dbGuard.IsAvailable)
        {
            return [];
        }

        const string sql = @"
SELECT DISTINCT TRIM(InternName)
FROM DonationData
WHERE InternName IS NOT NULL
  AND TRIM(InternName) <> ''";

        await using var connection = new MySqlConnection(ConnStr);
        var names = await connection.QueryAsync<string>(sql);
        return names.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
    }

    public async Task<Dictionary<string, string>> GetCurrentInternNamesByFundsAsync(IEnumerable<string> funds)
    {
        var fundList = funds
            .Where(fund => !string.IsNullOrWhiteSpace(fund))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (fundList.Length == 0 || !_dbGuard.IsAvailable)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        const string sql = @"
SELECT Fund, MAX(NULLIF(TRIM(InternName), '')) AS InternName
FROM DonationData
WHERE Fund IN @Funds
  AND InternName IS NOT NULL
  AND TRIM(InternName) <> ''
GROUP BY Fund";

        await using var connection = new MySqlConnection(ConnStr);
        var rows = await connection.QueryAsync<(string Fund, string InternName)>(sql, new { Funds = fundList });
        return rows.ToDictionary(row => row.Fund, row => row.InternName, StringComparer.OrdinalIgnoreCase);
    }

    private const string DonationProjection = @"
SELECT
    d.Id,
    d.DonorId,
    dn.DisplayName AS DonorDisplayName,
    d.Date,
    d.Frequency,
    d.AccountName,
    d.PaymentMethod,
    d.GiftType,
    d.Amount,
    d.Fund,
    d.InternName,
    d.PrimaryAddressee AS Addressee,
    d.SoftCreditName,
    d.DateCreated,
    d.IsAnonymous
FROM DonationData d
INNER JOIN Donors dn ON dn.Id = d.DonorId";

    public async Task<List<SubAccount>> GetSubAccountsByAccountIdAsync(int accountId)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<SubAccount>(
            "SELECT Id, AccountId, SubFund, Kind FROM SubAccounts WHERE AccountId = @AccountId",
            new { AccountId = accountId });
        var subAccounts = rows.ToList();
        _logger.LogInformation(
            "Subaccounts loaded: accountId={AccountId}, count={Count}, kinds={Kinds}",
            accountId,
            subAccounts.Count,
            string.Join("|", subAccounts.Select(s => s.Kind?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase)));
        return subAccounts;
    }

    public Task<List<DonationRecord>> GetDonationsForAccountTotalAsync(int accountId, string fundName, DateTime startDate, DateTime endDate)
    {
        return GetDonationsByAccountAndDateRangeAsync(accountId, fundName, startDate, endDate);
    }

    public async Task<List<DonationRecord>> GetDonationsForAccountScopeAsync(int accountId, string fundName, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<DonationRecord>(
            $@"{DonationProjection}
WHERE d.Date >= @StartDate
  AND d.Date <= @EndDate
  AND d.Fund COLLATE utf8mb4_0900_ai_ci IN (
      SELECT @Fund
      UNION
      SELECT SubFund
      FROM SubAccounts
      WHERE AccountId = @AccountId
  )",
            new { AccountId = accountId, Fund = fundName, StartDate = startDate, EndDate = endDate });
        return rows.ToList();
    }

    public Task<List<DonationRecord>> GetDonationsByFundsAsync(IEnumerable<string> fundNames)
    {
        _dbGuard.ThrowIfUnavailable();
        return LoadByFundsAsync(fundNames,
            $@"{DonationProjection}
WHERE d.Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
   OR d.Fund COLLATE utf8mb4_0900_ai_ci IN (
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
            $@"{DonationProjection}
WHERE d.Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
   OR d.Fund COLLATE utf8mb4_0900_ai_ci IN (
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
            $@"{DonationProjection}
WHERE d.Date >= @StartDate
  AND d.Date <= @EndDate
  AND (
       d.Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
       OR d.Fund COLLATE utf8mb4_0900_ai_ci IN (
            SELECT SubFund COLLATE utf8mb4_0900_ai_ci
            FROM SubAccounts
            WHERE AccountId = @AccountId AND Kind = 'Merged'
       )
  )",
            (fund, accountId) => new { Fund = fund, AccountId = accountId, StartDate = startDate, EndDate = endDate });
    }

    public async Task<List<DonationRecord>> GetDonationsByFundNamesAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        var funds = fundNames
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (funds.Length == 0)
        {
            return new List<DonationRecord>();
        }

        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<DonationRecord>(
            $@"{DonationProjection}
WHERE d.Date >= @StartDate
  AND d.Date <= @EndDate
  AND d.Fund COLLATE utf8mb4_0900_ai_ci IN @Funds",
            new { StartDate = startDate, EndDate = endDate, Funds = funds });
        var result = rows.ToList();
        _logger.LogInformation("Donations loaded in one fund query: fundCount={FundCount}, rowCount={RowCount}", funds.Length, result.Count);
        return result;
    }

    public async Task<List<DonationRecord>> GetDonationsByAccountAndDateRangeAsync(int accountId, string fundName, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = (await conn.QueryAsync<DonationRecord>(
            $@"{DonationProjection}
WHERE d.Date >= @StartDate
  AND d.Date <= @EndDate
  AND (
       d.Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
       OR d.Fund COLLATE utf8mb4_0900_ai_ci IN (
            SELECT SubFund COLLATE utf8mb4_0900_ai_ci
            FROM SubAccounts
            WHERE AccountId = @AccountId AND Kind = 'Merged'
       )
  )",
             new { AccountId = accountId, Fund = fundName, StartDate = startDate, EndDate = endDate })).ToList();

        if (rows.Count == 0)
        {
            var diagnostics = await conn.QuerySingleAsync<DonationQueryDiagnostics>(
                @"
SELECT
    (SELECT COUNT(*)
     FROM DonationData
     WHERE Date >= @StartDate AND Date <= @EndDate) AS DateRows,
    (SELECT COUNT(*)
     FROM DonationData d
     WHERE d.Date >= @StartDate
       AND d.Date <= @EndDate
       AND (
            d.Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
            OR d.Fund COLLATE utf8mb4_0900_ai_ci IN (
                 SELECT SubFund COLLATE utf8mb4_0900_ai_ci
                 FROM SubAccounts
                 WHERE AccountId = @AccountId AND Kind = 'Merged'
            )
       )) AS FundDateRows,
    (SELECT COUNT(*)
     FROM DonationData d
     INNER JOIN Donors dn ON dn.Id = d.DonorId
     WHERE d.Date >= @StartDate
       AND d.Date <= @EndDate
       AND (
            d.Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
            OR d.Fund COLLATE utf8mb4_0900_ai_ci IN (
                 SELECT SubFund COLLATE utf8mb4_0900_ai_ci
                 FROM SubAccounts
                 WHERE AccountId = @AccountId AND Kind = 'Merged'
            )
       )) AS DonorJoinRows",
                new { AccountId = accountId, Fund = fundName, StartDate = startDate, EndDate = endDate });

            _logger.LogWarning(
                "Donation query returned no rows: accountId={AccountId} range={StartDate:yyyy-MM-dd HH:mm:ss}..{EndDate:yyyy-MM-dd HH:mm:ss} dateRows={DateRows} fundDateRows={FundDateRows} donorJoinRows={DonorJoinRows}",
                accountId,
                startDate,
                endDate,
                diagnostics.DateRows,
                diagnostics.FundDateRows,
                diagnostics.DonorJoinRows);
        }

        return rows;
    }

    private sealed class DonationQueryDiagnostics
    {
        public int DateRows { get; init; }
        public int FundDateRows { get; init; }
        public int DonorJoinRows { get; init; }
    }

    public Task<List<DonationRecord>> GetDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string donorName)
    {
        _dbGuard.ThrowIfUnavailable();
        return LoadByFundsAsync(fundNames,
            $@"{DonationProjection}
WHERE dn.DisplayName = @DonorName
  AND (
       d.Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
       OR d.Fund COLLATE utf8mb4_0900_ai_ci IN (
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
            $@"{DonationProjection}
WHERE dn.DisplayName = @DonorName
  AND (
       d.Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
       OR d.Fund COLLATE utf8mb4_0900_ai_ci IN (
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
            $@"{DonationProjection}
WHERE dn.DisplayName LIKE @SearchTerm
  AND (
       d.Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
       OR d.Fund COLLATE utf8mb4_0900_ai_ci IN (
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
            $@"{DonationProjection}
WHERE dn.DisplayName LIKE @SearchTerm
  AND (
       d.Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
       OR d.Fund COLLATE utf8mb4_0900_ai_ci IN (
            SELECT SubFund COLLATE utf8mb4_0900_ai_ci
            FROM SubAccounts
            WHERE AccountId = @AccountId AND Kind = 'Merged'
       )
  )",
            new { AccountId = accountId, Fund = fundName, SearchTerm = likeTerm });
        return rows.ToList();
    }

    public async Task<List<DonationRecord>> GetInternDonationsByInternNameAndDateRangeAsync(string internName, DateTime startDate, DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();

        if (string.IsNullOrWhiteSpace(internName))
        {
            return new List<DonationRecord>();
        }

        var criteria = InternAccountUtility.CreateDesignationCriteria(internName);

        await using var conn = new MySqlConnection(ConnStr);

        var exactMatchCount = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*)
FROM DonationData
WHERE Date >= @StartDate
  AND Date <= @EndDate
  AND InternName COLLATE utf8mb4_0900_ai_ci = @InternName COLLATE utf8mb4_0900_ai_ci",
            new
            {
                StartDate = startDate,
                EndDate = endDate,
                InternName = criteria.InternDesignationName
            });

        var alternateMatchCount = criteria.HasAlternateDesignation
            ? await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
FROM DonationData
WHERE Date >= @StartDate
  AND Date <= @EndDate
  AND InternName COLLATE utf8mb4_0900_ai_ci = @AlternateDesignation COLLATE utf8mb4_0900_ai_ci",
                new
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    AlternateDesignation = criteria.AlternateDesignation
                })
            : 0;

        var tokenMatchCount = criteria.HasNameTokens
            ? await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
FROM DonationData
WHERE Date >= @StartDate
  AND Date <= @EndDate
  AND InternName IS NOT NULL
  AND (
      InternName COLLATE utf8mb4_0900_ai_ci LIKE @FirstToken COLLATE utf8mb4_0900_ai_ci
      OR InternName COLLATE utf8mb4_0900_ai_ci LIKE @LastToken COLLATE utf8mb4_0900_ai_ci
  )",
                new
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    FirstToken = $"%{criteria.FirstName}%",
                    LastToken = $"%{criteria.LastName}%"
                })
            : 0;

        var normalizedMatchCount = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*)
FROM DonationData
WHERE Date >= @StartDate
  AND Date <= @EndDate
  AND (
      LOWER(REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(InternName,''), ' ', ''), ',', ''), '.', ''), '-', '')) = @DesignationLookupKey
      OR (@HasAlternateLookupKey = 1 AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(InternName,''), ' ', ''), ',', ''), '.', ''), '-', '')) = @AlternateLookupKey)
  )",
            new
            {
                StartDate = startDate,
                EndDate = endDate,
                DesignationLookupKey = criteria.DesignationLookupKey,
                AlternateLookupKey = criteria.AlternateLookupKey,
                HasAlternateLookupKey = criteria.HasAlternateLookupKey ? 1 : 0
            });

        _logger.LogInformation(
            "Intern donation query debug: exactMatches={ExactMatches}, alternateMatches={AlternateMatches}, tokenMatches={TokenMatches}, normalizedMatches={NormalizedMatches}, range={StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}",
            exactMatchCount,
            alternateMatchCount,
            tokenMatchCount,
            normalizedMatchCount,
            startDate,
            endDate);

        var rows = await conn.QueryAsync<DonationRecord>(
            $@"{DonationProjection}
WHERE d.Date >= @StartDate
  AND d.Date <= @EndDate
  AND (
      d.InternName COLLATE utf8mb4_0900_ai_ci = @InternName COLLATE utf8mb4_0900_ai_ci
      OR (@HasAlternateDesignation = 1 AND d.InternName COLLATE utf8mb4_0900_ai_ci = @AlternateDesignation COLLATE utf8mb4_0900_ai_ci)
      OR (
          LOCATE(',', d.InternName) > 0
          AND TRIM(CONCAT(
              TRIM(SUBSTRING_INDEX(d.InternName, ',', -1)),
              ' ',
              TRIM(SUBSTRING_INDEX(d.InternName, ',', 1))
          )) COLLATE utf8mb4_0900_ai_ci = @InternName COLLATE utf8mb4_0900_ai_ci
      )
      OR (
          @HasAlternateDesignation = 1
          AND LOCATE(',', d.InternName) > 0
          AND TRIM(CONCAT(
              TRIM(SUBSTRING_INDEX(d.InternName, ',', -1)),
              ' ',
              TRIM(SUBSTRING_INDEX(d.InternName, ',', 1))
          )) COLLATE utf8mb4_0900_ai_ci = @AlternateDesignation COLLATE utf8mb4_0900_ai_ci
      )
      OR (
          @HasNameTokens = 1
          AND d.InternName IS NOT NULL
          AND d.InternName COLLATE utf8mb4_0900_ai_ci LIKE @FirstToken COLLATE utf8mb4_0900_ai_ci
          AND d.InternName COLLATE utf8mb4_0900_ai_ci LIKE @LastToken COLLATE utf8mb4_0900_ai_ci
      )
      OR (
          LOWER(REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(d.InternName,''), ' ', ''), ',', ''), '.', ''), '-', '')) = @DesignationLookupKey
      )
      OR (
          @HasAlternateLookupKey = 1
          AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(d.InternName,''), ' ', ''), ',', ''), '.', ''), '-', '')) = @AlternateLookupKey
      )
  )",
            new
            {
                StartDate = startDate,
                EndDate = endDate,
                InternName = criteria.InternDesignationName,
                AlternateDesignation = criteria.AlternateDesignation,
                HasAlternateDesignation = criteria.HasAlternateDesignation ? 1 : 0,
                HasNameTokens = criteria.HasNameTokens ? 1 : 0,
                FirstToken = $"%{criteria.FirstName}%",
                LastToken = $"%{criteria.LastName}%",
                DesignationLookupKey = criteria.DesignationLookupKey,
                AlternateLookupKey = criteria.AlternateLookupKey,
                HasAlternateLookupKey = criteria.HasAlternateLookupKey ? 1 : 0
            });

        var rowList = rows.ToList();
        _logger.LogInformation(
            "Intern donation query returned {RowCount} rows",
            rowList.Count);

        return rowList;
    }

    public async Task<IReadOnlyDictionary<int, List<DonationRecord>>> GetDonationsForAccountsAsync(
        IReadOnlyList<(int AccountId, string Fund, IReadOnlyList<string> MergedSubFunds)> accounts,
        DateTime startDate,
        DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();

        var scopedAccounts = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.Fund))
            .Distinct()
            .ToList();

        var result = scopedAccounts
            .Select(account => account.AccountId)
            .Distinct()
            .ToDictionary(accountId => accountId, _ => new List<DonationRecord>());

        if (scopedAccounts.Count == 0)
        {
            return result;
        }

        var fundsByAccountId = scopedAccounts.ToDictionary(
            account => account.AccountId,
            account => account.MergedSubFunds
                .Append(account.Fund)
                .Where(fund => !string.IsNullOrWhiteSpace(fund))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var accountIdsByFund = fundsByAccountId
            .SelectMany(pair => pair.Value.Select(fund => (Fund: fund, pair.Key)))
            .GroupBy(item => item.Fund, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Key).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var funds = accountIdsByFund.Keys.ToArray();

        const string sql = $@"{DonationProjection}
WHERE d.Date >= @StartDate
  AND d.Date <= @EndDate
  AND d.Fund COLLATE utf8mb4_0900_ai_ci IN @Funds";

        await using var connection = new MySqlConnection(ConnStr);
        var rows = (await connection.QueryAsync<DonationRecord>(
            sql,
            new { Funds = funds, StartDate = startDate, EndDate = endDate })).ToList();

        foreach (var row in rows)
        {
            if (!accountIdsByFund.TryGetValue(row.Fund ?? string.Empty, out var accountIds))
            {
                continue;
            }

            foreach (var accountId in accountIds)
            {
                result[accountId].Add(row);
            }
        }

        _logger.LogInformation(
            "Normal account donations loaded in one batch: accountCount={AccountCount}, fundCount={FundCount}, rowCount={RowCount}",
            scopedAccounts.Count,
            funds.Length,
            rows.Count);

        return result;
    }

    public async Task<IReadOnlyDictionary<string, List<DonationRecord>>> GetInternDonationsByNamesAndDateRangeAsync(
        IReadOnlyList<string> internNames,
        DateTime startDate,
        DateTime endDate)
    {
        _dbGuard.ThrowIfUnavailable();

        var names = internNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = names.ToDictionary(name => name, _ => new List<DonationRecord>(), StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return result;
        }

        const string sql = $@"{DonationProjection}
WHERE d.Date >= @StartDate
  AND d.Date <= @EndDate
  AND d.InternName COLLATE utf8mb4_0900_ai_ci IN @InternNames";

        await using var conn = new MySqlConnection(ConnStr);
        var rows = (await conn.QueryAsync<DonationRecord>(
            sql,
            new
            {
                StartDate = startDate,
                EndDate = endDate,
                InternNames = names
            })).ToList();

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.InternName) && result.TryGetValue(row.InternName.Trim(), out var donations))
            {
                donations.Add(row);
            }
        }

        _logger.LogInformation(
            "Intern donations loaded in bulk: requestedCount={RequestedCount}, rowCount={RowCount}, range={StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}",
            names.Count,
            rows.Count,
            startDate,
            endDate);

        return result;
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
        var results = new Dictionary<int, DonationRecord>();
        foreach (var fund in funds)
        {
            var accountId = await conn.ExecuteScalarAsync<int?>(
                "SELECT AccountId FROM Accounts WHERE Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci LIMIT 1",
                new { Fund = fund });

            var rows = await conn.QueryAsync<DonationRecord>(sql, parameterFactory(fund, accountId));
            foreach (var row in rows)
            {
                results[row.Id] = row;
            }
        }

        return results.Values.ToList();
    }
}
