using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class DonationReadRepository : IDonationReadRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;

    public DonationReadRepository(IConfiguration configuration, IDatabaseGuard dbGuard)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
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
