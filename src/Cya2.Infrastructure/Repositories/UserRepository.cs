using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _dbGuard;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(IConfiguration configuration, IDatabaseGuard dbGuard, ILogger<UserRepository> logger)
    {
        _configuration = configuration;
        _dbGuard = dbGuard;
        _logger = logger;
    }

    private string ConnStr => _configuration.GetConnectionString("default") ?? string.Empty;

    public async Task<User?> GetByIdAsync(int id)
    {
        _dbGuard.ThrowIfUnavailable();
        // var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        const string sql = "SELECT Id, GoogleId, Email, Name, Language, AuthLevel, DefaultAccount FROM Users WHERE Id = @Id LIMIT 1";
        await using var conn = new MySqlConnection(ConnStr);
        var user = await conn.QueryFirstOrDefaultAsync<User>(sql, new { Id = id });
        // _logger.LogInformation("UserRepository.GetById phase=query-complete elapsedMs={ElapsedMs} userId={UserId} found={Found}", stopwatch.ElapsedMilliseconds, id, user is not null);
        return user;
    }

    public async Task<List<User>> GetAllAsync()
    {
        _dbGuard.ThrowIfUnavailable();
        try
        {
            const string sql = "SELECT Id, GoogleId, Email, Name, Language, AuthLevel, DefaultAccount FROM Users ORDER BY Name";
            await using var conn = new MySqlConnection(ConnStr);
            var rows = await conn.QueryAsync<User>(sql);
            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UserRepository.GetAllAsync failed.");
            return new List<User>();
        }
    }

    public async Task<User> AddAsync(User entity)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
INSERT INTO Users (GoogleId, Email, Name, Language, AuthLevel, DefaultAccount)
VALUES (@GoogleId, @Email, @Name, @Language, @AuthLevel, @DefaultAccount)";
        await using var conn = new MySqlConnection(ConnStr);
        await conn.ExecuteAsync(sql, new
        {
            entity.GoogleId, entity.Email, entity.Name, entity.Language,
            entity.AuthLevel, entity.DefaultAccount
        });
        return await GetByEmailAsync(entity.Email) ?? entity;
    }

    public async Task<User> UpdateAsync(User entity)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = @"
UPDATE Users SET GoogleId = @GoogleId, Email = @Email, Name = @Name,
    Language = @Language, AuthLevel = @AuthLevel, DefaultAccount = @DefaultAccount
WHERE Id = @Id";
        await using var conn = new MySqlConnection(ConnStr);
        await conn.ExecuteAsync(sql, new
        {
            entity.Id, entity.GoogleId, entity.Email, entity.Name,
            entity.Language, entity.AuthLevel, entity.DefaultAccount
        });
        return await GetByIdAsync(entity.Id) ?? entity;
    }

    public async Task DeleteAsync(int id)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        await conn.ExecuteAsync("DELETE FROM Users WHERE Id = @Id", new { Id = id });
    }

    public async Task<bool> ExistsAsync(int id)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Id = @Id", new { Id = id });
        return count > 0;
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = "SELECT Id, GoogleId, Email, Name, Language, AuthLevel, DefaultAccount FROM Users WHERE Email = @Email LIMIT 1";
        await using var conn = new MySqlConnection(ConnStr);
        return await conn.QueryFirstOrDefaultAsync<User>(sql, new { Email = email });
    }

    public async Task<User?> GetByExternalIdAsync(string externalId)
    {
        _dbGuard.ThrowIfUnavailable();
        const string sql = "SELECT Id, GoogleId, Email, Name, Language, AuthLevel, DefaultAccount FROM Users WHERE GoogleId = @GoogleId LIMIT 1";
        await using var conn = new MySqlConnection(ConnStr);
        return await conn.QueryFirstOrDefaultAsync<User>(sql, new { GoogleId = externalId });
    }

    public Task<List<User>> GetActiveUsersAsync() => GetAllAsync();

    public async Task<bool> ExistsAsync(string email)
    {
        _dbGuard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(ConnStr);
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Email = @Email", new { Email = email });
        return count > 0;
    }
}
