using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.Enums;
using Cya2.Core.ValueObjects;

namespace Cya2.Infrastructure.Data.Repositories;

public class UserRepository : BaseRepository, IUserRepository
{
    public UserRepository(IConfiguration configuration, ILogger<UserRepository> logger) 
        : base(configuration, logger)
    {
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT Id, Email, Name, AuthLevel, Language, DefaultAccount
                FROM Users 
                WHERE Email = @Email
                LIMIT 1";

            var userData = await connection.QueryFirstOrDefaultAsync(sql, new { Email = email });
            
            if (userData == null)
                return null;

            var authLevel = ParseUserRole(userData.AuthLevel?.ToString());
            var user = new User(
                userData.Email?.ToString() ?? email,
                userData.Name?.ToString() ?? "",
                authLevel
            );

            // Set ID using reflection
            var idProperty = typeof(User).BaseType?.GetProperty("Id");
            idProperty?.SetValue(user, userData.Id);

            // Set language if available
            if (!string.IsNullOrWhiteSpace(userData.Language?.ToString()))
            {
                user.UpdateProfile(user.Name, userData.Language.ToString());
            }

            // Set default account if available
            if (userData.DefaultAccount != null && int.TryParse(userData.DefaultAccount.ToString(), out int defaultAccountId))
            {
                var defaultAccount = await GetAccountById(connection, defaultAccountId);
                if (defaultAccount != null)
                {
                    user.SetDefaultAccount(defaultAccount);
                }
            }

            // Load user account access
            await LoadUserAccountAccess(connection, user);

            return user;
        });
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT Id, Email, Name, AuthLevel, Language, DefaultAccount
                FROM Users 
                WHERE Id = @Id
                LIMIT 1";

            var userData = await connection.QueryFirstOrDefaultAsync(sql, new { Id = id });
            
            if (userData == null)
                return null;

            var authLevel = ParseUserRole(userData.AuthLevel?.ToString());
            var user = new User(
                userData.Email?.ToString() ?? "",
                userData.Name?.ToString() ?? "",
                authLevel
            );

            // Set ID using reflection
            var idProperty = typeof(User).BaseType?.GetProperty("Id");
            idProperty?.SetValue(user, userData.Id);

            // Set language if available
            if (!string.IsNullOrWhiteSpace(userData.Language?.ToString()))
            {
                user.UpdateProfile(user.Name, userData.Language.ToString());
            }

            // Set default account if available
            if (userData.DefaultAccount != null && int.TryParse(userData.DefaultAccount.ToString(), out int defaultAccountId))
            {
                var defaultAccount = await GetAccountById(connection, defaultAccountId);
                if (defaultAccount != null)
                {
                    user.SetDefaultAccount(defaultAccount);
                }
            }

            // Load user account access
            await LoadUserAccountAccess(connection, user);

            return user;
        });
    }

    public async Task<List<User>> GetAllAsync()
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT Id, Email, Name, AuthLevel, Language, DefaultAccount
                FROM Users 
                ORDER BY Name";

            var usersData = await connection.QueryAsync(sql);
            var users = new List<User>();

            foreach (var userData in usersData)
            {
                var user = await GetByIdAsync(userData.Id);
                if (user != null)
                    users.Add(user);
            }

            return users;
        });
    }

    public async Task<User> AddAsync(User user)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                INSERT INTO Users (Email, Name, AuthLevel, Language, DefaultAccount)
                VALUES (@Email, @Name, @AuthLevel, @Language, @DefaultAccount);
                SELECT LAST_INSERT_ID();";

            var parameters = new
            {
                Email = user.Email,
                Name = user.Name,
                AuthLevel = user.AuthLevel.ToString(),
                Language = user.Language,
                DefaultAccount = user.DefaultAccountId
            };

            var newId = await connection.QueryFirstAsync<int>(sql, parameters);
            
            // Set ID using reflection
            var idProperty = typeof(User).BaseType?.GetProperty("Id");
            idProperty?.SetValue(user, newId);

            _logger.LogInformation("Created user {UserId} with email {Email}", newId, user.Email);
            return user;
        });
    }

    public async Task<User> UpdateAsync(User user)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                UPDATE Users 
                SET Email = @Email, Name = @Name, AuthLevel = @AuthLevel, 
                    Language = @Language, DefaultAccount = @DefaultAccount
                WHERE Id = @Id";

            var parameters = new
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.Name,
                AuthLevel = user.AuthLevel.ToString(),
                Language = user.Language,
                DefaultAccount = user.DefaultAccountId
            };

            var affectedRows = await connection.ExecuteAsync(sql, parameters);
            
            if (affectedRows == 0)
                throw new ArgumentException($"User with ID {user.Id} not found");

            _logger.LogInformation("Updated user {UserId}", user.Id);
            return user;
        });
    }

    public async Task DeleteAsync(int id)
    {
        await ExecuteWithRetryAsync(async connection =>
        {
            // First delete user account access
            await connection.ExecuteAsync("DELETE FROM AccountsUsers WHERE UserId = @Id", new { Id = id });
            
            // Then delete the user
            const string sql = "DELETE FROM Users WHERE Id = @Id";
            
            var affectedRows = await connection.ExecuteAsync(sql, new { Id = id });
            
            if (affectedRows == 0)
                throw new ArgumentException($"User with ID {id} not found");

            _logger.LogInformation("Deleted user {UserId}", id);
        });
    }

    public async Task<bool> ExistsAsync(string email)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            const string sql = @"
                SELECT COUNT(1) 
                FROM Users 
                WHERE Email = @Email";

            var count = await connection.QueryFirstOrDefaultAsync<int>(sql, new { Email = email });
            return count > 0;
        });
    }

    private async Task<Account?> GetAccountById(System.Data.IDbConnection connection, int accountId)
    {
        const string sql = @"
            SELECT AccountId as Id, Fund as FundCode, Fund as Name
            FROM Accounts 
            WHERE AccountId = @AccountId
            LIMIT 1";

        var accountData = await connection.QueryFirstOrDefaultAsync(sql, new { AccountId = accountId });
        
        if (accountData == null)
            return null;

        var account = new Account(
            accountData.FundCode?.ToString() ?? "",
            accountData.Name?.ToString() ?? "",
            AccountType.Primary
        );

        // Set ID using reflection
        var idProperty = typeof(Account).BaseType?.GetProperty("Id");
        idProperty?.SetValue(account, accountData.Id);

        return account;
    }

    private async Task LoadUserAccountAccess(System.Data.IDbConnection connection, User user)
    {
        const string sql = @"
            SELECT au.AccountId, a.Fund as FundCode, a.Fund as Name
            FROM AccountsUsers au
            INNER JOIN Accounts a ON au.AccountId = a.AccountId
            WHERE au.UserId = @UserId";

        var accessData = await connection.QueryAsync(sql, new { UserId = user.Id });

        foreach (var access in accessData)
        {
            var account = new Account(
                access.FundCode?.ToString() ?? "",
                access.Name?.ToString() ?? "",
                AccountType.Primary
            );

            // Set ID using reflection
            var idProperty = typeof(Account).BaseType?.GetProperty("Id");
            idProperty?.SetValue(account, access.AccountId);

            user.GrantAccountAccess(account);
        }
    }

    private static UserRole ParseUserRole(string? authLevel)
    {
        return authLevel?.ToLowerInvariant() switch
        {
            "admin" => UserRole.Admin,
            "viewer" => UserRole.Viewer,
            "user" => UserRole.User,
            _ => UserRole.User
        };
    }
}