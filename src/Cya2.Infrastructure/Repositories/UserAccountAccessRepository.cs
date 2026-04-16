using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class UserAccountAccessRepository : IUserAccountAccessRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;

    public UserAccountAccessRepository(IConfiguration configuration, IDatabaseGuard dbGuard)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
    }

    private string ConnStr => _configuration.GetConnectionString("default") ?? string.Empty;

    public async Task<List<Account>> GetUserAccountsAsync(int userId)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
SELECT a.AccountId, a.Fund, a.AccountingClass, a.AccountNumber, a.CreatedAt, a.Overhead, a.SoftCredit, a.BalanceAdjustment
FROM Accounts a
INNER JOIN AccountsUsers au ON a.AccountId = au.AccountId
WHERE au.UserId = @UserId
ORDER BY a.Fund";
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<Account>(sql, new { UserId = userId });
        return rows.ToList();
    }

    public async Task<Account?> GetAccountByIdAsync(int accountId)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
SELECT AccountId, Fund, AccountingClass, AccountNumber, CreatedAt, Overhead, SoftCredit, BalanceAdjustment
FROM Accounts WHERE AccountId = @AccountId LIMIT 1";
        await using var conn = new MySqlConnection(ConnStr);
        return await conn.QueryFirstOrDefaultAsync<Account>(sql, new { AccountId = accountId });
    }

    public async Task<bool> HasAccessAsync(int userId, int accountId)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AccountsUsers WHERE UserId = @UserId AND AccountId = @AccountId",
            new { UserId = userId, AccountId = accountId });
        return count > 0;
    }

    public async Task<bool> GrantAccessAsync(int userId, int accountId)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.ExecuteAsync(
            "INSERT INTO AccountsUsers (UserId, AccountId) VALUES (@UserId, @AccountId)",
            new { UserId = userId, AccountId = accountId });
        return rows > 0;
    }

    public async Task<bool> RevokeAccessAsync(int userId, int accountId)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.ExecuteAsync(
            "DELETE FROM AccountsUsers WHERE UserId = @UserId AND AccountId = @AccountId",
            new { UserId = userId, AccountId = accountId });
        return rows > 0;
    }

    public async Task<bool> RevokeAllAccessAsync(int userId)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        await conn.ExecuteAsync("DELETE FROM AccountsUsers WHERE UserId = @UserId", new { UserId = userId });
        return true;
    }

    public async Task<int> GetUserAccountCountAsync(int userId)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AccountsUsers WHERE UserId = @UserId", new { UserId = userId });
    }

    public async Task<bool> SetUserDefaultAccountAsync(int userId, int? accountId)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        int rows;
        if (accountId.HasValue)
            rows = await conn.ExecuteAsync(
                "UPDATE Users SET DefaultAccount = @DefaultAccount WHERE Id = @UserId",
                new { UserId = userId, DefaultAccount = accountId.Value });
        else
            rows = await conn.ExecuteAsync(
                "UPDATE Users SET DefaultAccount = NULL WHERE Id = @UserId",
                new { UserId = userId });
        return rows > 0;
    }
}
