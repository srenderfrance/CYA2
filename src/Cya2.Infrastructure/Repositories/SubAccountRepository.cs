using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class SubAccountRepository : ISubAccountRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;

    public SubAccountRepository(IConfiguration configuration, IDatabaseGuard dbGuard)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
    }

    private string ConnStr => _configuration.GetConnectionString("default") ?? string.Empty;

    public async Task<List<SubAccount>> GetAllAsync()
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<SubAccount>("SELECT Id, AccountId, SubFund, Kind FROM SubAccounts");
        return rows.ToList();
    }

    public async Task<List<SubAccount>> GetByAccountIdAsync(int accountId)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.QueryAsync<SubAccount>(
            "SELECT Id, AccountId, SubFund, Kind FROM SubAccounts WHERE AccountId = @AccountId",
            new { AccountId = accountId });
        return rows.ToList();
    }

    public async Task<SubAccount?> GetByIdAsync(int id)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        return await conn.QueryFirstOrDefaultAsync<SubAccount>(
            "SELECT Id, AccountId, SubFund, Kind FROM SubAccounts WHERE Id = @Id LIMIT 1",
            new { Id = id });
    }

    public async Task<bool> ExistsByNameAsync(int accountId, string subFund, int? excludeId = null)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SubAccounts WHERE AccountId = @AccountId AND SubFund = @SubFund AND (@ExcludeId IS NULL OR Id <> @ExcludeId)",
            new { AccountId = accountId, SubFund = subFund, ExcludeId = excludeId });
        return count > 0;
    }

    public async Task<SubAccount> AddAsync(SubAccount entity)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        await conn.ExecuteAsync(
            "INSERT INTO SubAccounts (AccountId, SubFund, Kind) VALUES (@AccountId, @SubFund, @Kind)",
            new { entity.AccountId, entity.SubFund, entity.Kind });
        return await conn.QueryFirstOrDefaultAsync<SubAccount>(
            "SELECT Id, AccountId, SubFund, Kind FROM SubAccounts WHERE AccountId = @AccountId AND SubFund = @SubFund AND Kind = @Kind ORDER BY Id DESC LIMIT 1",
            new { entity.AccountId, entity.SubFund, entity.Kind }) ?? entity;
    }

    public async Task<SubAccount> UpdateAsync(SubAccount entity)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        await conn.ExecuteAsync(
            "UPDATE SubAccounts SET SubFund = @SubFund, Kind = @Kind WHERE Id = @Id",
            new { entity.Id, entity.SubFund, entity.Kind });
        return await GetByIdAsync(entity.Id) ?? entity;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var rows = await conn.ExecuteAsync("DELETE FROM SubAccounts WHERE Id = @Id", new { Id = id });
        return rows > 0;
    }
}
