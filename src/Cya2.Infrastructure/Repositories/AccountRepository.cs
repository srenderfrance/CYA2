using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class AccountRepository : IAccountRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;

    public AccountRepository(IConfiguration configuration, IDatabaseGuard dbGuard)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
    }

    private string ConnStr => _configuration.GetConnectionString("default") ?? string.Empty;

    public async Task<Account?> GetByIdAsync(int id)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = "SELECT AccountId, Fund, AccountingClass, AccountNumber, CreatedAt, Overhead, SoftCredit, BalanceAdjustment FROM Accounts WHERE AccountId = @Id LIMIT 1";
        await using var conn = new MySqlConnection(ConnStr);
        return await conn.QueryFirstOrDefaultAsync<Account>(sql, new { Id = id });
    }

    public async Task<List<Account>> GetAllAsync()
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = "SELECT AccountId, Fund, AccountingClass, AccountNumber, CreatedAt, Overhead, SoftCredit, BalanceAdjustment FROM Accounts ORDER BY Fund";
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<Account>(sql);
        return rows.ToList();
    }

    public async Task<Account> AddAsync(Account entity)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
INSERT INTO Accounts (Fund, AccountingClass, AccountNumber, CreatedAt, Overhead, SoftCredit, BalanceAdjustment)
VALUES (@Fund, @AccountingClass, @AccountNumber, @CreatedAt, @Overhead, @SoftCredit, @BalanceAdjustment)";
        await using var conn = new MySqlConnection(ConnStr);
        await conn.ExecuteAsync(sql, new
        {
            entity.Fund, entity.AccountingClass, entity.AccountNumber,
            entity.CreatedAt, entity.Overhead, entity.SoftCredit, entity.BalanceAdjustment
        });
        return await GetByFundAsync(entity.Fund) ?? entity;
    }

    public async Task<Account> UpdateAsync(Account entity)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
UPDATE Accounts
SET Fund = @Fund, AccountingClass = @AccountingClass, AccountNumber = @AccountNumber,
    Overhead = @Overhead, SoftCredit = @SoftCredit, BalanceAdjustment = @BalanceAdjustment
WHERE AccountId = @AccountId";
        await using var conn = new MySqlConnection(ConnStr);
        await conn.ExecuteAsync(sql, new
        {
            entity.AccountId, entity.Fund, entity.AccountingClass, entity.AccountNumber,
            entity.Overhead, entity.SoftCredit, entity.BalanceAdjustment
        });
        return await GetByIdAsync(entity.AccountId) ?? entity;
    }

    public async Task DeleteAsync(int id)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        await conn.ExecuteAsync("DELETE FROM Accounts WHERE AccountId = @Id", new { Id = id });
    }

    public async Task<bool> ExistsAsync(int id)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Accounts WHERE AccountId = @Id", new { Id = id });
        return count > 0;
    }

    public Task<Account?> GetByFundCodeAsync(string fundCode) => GetByFundAsync(fundCode);

    public async Task<Account?> GetByFundAsync(string fund)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = "SELECT AccountId, Fund, AccountingClass, AccountNumber, CreatedAt, Overhead, SoftCredit, BalanceAdjustment FROM Accounts WHERE Fund = @Fund LIMIT 1";
        await using var conn = new MySqlConnection(ConnStr);
        return await conn.QueryFirstOrDefaultAsync<Account>(sql, new { Fund = fund });
    }

    public async Task<Account?> GetByAccountNumberAsync(string accountNumber)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = "SELECT AccountId, Fund, AccountingClass, AccountNumber, CreatedAt, Overhead, SoftCredit, BalanceAdjustment FROM Accounts WHERE AccountNumber = @AccountNumber LIMIT 1";
        await using var conn = new MySqlConnection(ConnStr);
        return await conn.QueryFirstOrDefaultAsync<Account>(sql, new { AccountNumber = accountNumber });
    }

    public async Task<List<Account>> GetByUserIdAsync(string userId)
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

    public async Task<bool> ValidateUserAccessAsync(string userId, string fund)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
SELECT COUNT(*) FROM Accounts a
INNER JOIN AccountsUsers au ON a.AccountId = au.AccountId
WHERE au.UserId = @UserId AND a.Fund = @Fund";
        await using var conn = new MySqlConnection(ConnStr);
        var count = await conn.ExecuteScalarAsync<int>(sql, new { UserId = userId, Fund = fund });
        return count > 0;
    }

    public async Task<bool> ExistsAsync(string fundCode)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Accounts WHERE Fund = @Fund", new { Fund = fundCode });
        return count > 0;
    }
}
