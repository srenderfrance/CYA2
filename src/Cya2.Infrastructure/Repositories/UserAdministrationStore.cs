using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.Utilities;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class UserAdministrationStore(IConfiguration configuration, IDatabaseGuard guard) : IUserAdministrationStore
{
    private static UserAdministrationResult Result(UserAdministrationStatus status, bool intern = false) => new(status, intern);

    public Task<UserAdministrationResult> CreateAsync(CreateUserCommand command) => ExecuteAsync(command.ActorId, async (conn, tx) =>
    {
        if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Email=@Email", new { command.Email }, tx) != 0)
            return Result(UserAdministrationStatus.DuplicateEmail);

        var intern = string.Equals(command.AuthLevel, "Intern", StringComparison.OrdinalIgnoreCase);
        if (intern && !command.ReuseInternAccount && await FindAccountAsync(conn, tx, InternAccountUtility.BuildInternFund(command.Name)) is not null)
            return Result(UserAdministrationStatus.InternReuseRequired);

        if (!intern && !await AccountsExistAsync(conn, tx, command.AccountIds))
            return Result(UserAdministrationStatus.InvalidAccount);

        var id = await conn.ExecuteScalarAsync<int>("INSERT INTO Users (GoogleId, Email, Name, Language, AuthLevel) VALUES (NULL, @Email, @Name, 'en-US', @AuthLevel); SELECT LAST_INSERT_ID();", command, tx);
        if (intern)
        {
            var status = await EnsureInternAsync(conn, tx, id, command.Name, null, command.ReuseInternAccount);
            if (status != UserAdministrationStatus.Success) return Result(status);
        }
        else
        {
            foreach (var accountId in command.AccountIds.Distinct())
                await conn.ExecuteAsync("INSERT INTO AccountsUsers (UserId, AccountId) VALUES (@id, @accountId)", new { id, accountId }, tx);
            if (command.AccountIds.Distinct().Count() == 1)
                await conn.ExecuteAsync("UPDATE Users SET DefaultAccount=@accountId WHERE Id=@id", new { id, accountId = command.AccountIds[0] }, tx);
        }
        return Result(UserAdministrationStatus.Success, intern);
    });

    public Task<UserAdministrationResult> UpdateAsync(UpdateUserCommand command) => ExecuteAsync(command.ActorId, async (conn, tx) =>
    {
        var user = await conn.QuerySingleOrDefaultAsync<User>("SELECT Id, Name, Email, AuthLevel, DefaultAccount FROM Users WHERE Id=@UserId FOR UPDATE", command, tx);
        if (user is null) return Result(UserAdministrationStatus.NotFound);
        var name = string.IsNullOrWhiteSpace(command.Name) ? user.Name : command.Name.Trim();
        var email = string.IsNullOrWhiteSpace(command.Email) ? user.Email : command.Email.Trim();
        var role = string.IsNullOrWhiteSpace(command.AuthLevel) ? user.AuthLevel : command.AuthLevel.Trim();
        if (user.IsIntern() && !string.Equals(role, user.AuthLevel, StringComparison.OrdinalIgnoreCase))
            return Result(UserAdministrationStatus.InternRoleChange);
        if (user.IsAdmin() && !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            if (command.ActorId == command.UserId) return Result(UserAdministrationStatus.SelfChange);
            if (await AdminCountAsync(conn, tx) <= 1) return Result(UserAdministrationStatus.LastAdmin);
        }
        if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Email=@email AND Id<>@UserId", new { email, command.UserId }, tx) != 0)
            return Result(UserAdministrationStatus.DuplicateEmail);
        var accountIds = new[] { command.GrantAccountId, command.RevokeAccountId }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        if (!await AccountsExistAsync(conn, tx, accountIds)) return Result(UserAdministrationStatus.InvalidAccount);
        if (command.GrantAccountId.HasValue && command.GrantAccountId == command.RevokeAccountId) return Result(UserAdministrationStatus.InvalidAccount);

        var intern = string.Equals(role, "Intern", StringComparison.OrdinalIgnoreCase);
        if (intern)
        {
            var status = await EnsureInternAsync(conn, tx, user.Id, name, user.Name, command.ReuseInternAccount);
            if (status != UserAdministrationStatus.Success) return Result(status);
        }
        await conn.ExecuteAsync("UPDATE Users SET Name=@name, Email=@email, AuthLevel=@role WHERE Id=@UserId", new { name, email, role, command.UserId }, tx);
        if (command.GrantAccountId is int grant)
        {
            await conn.ExecuteAsync("INSERT INTO AccountsUsers (UserId, AccountId) VALUES (@UserId, @grant) ON DUPLICATE KEY UPDATE AccountId=VALUES(AccountId)", new { command.UserId, grant }, tx);
            await conn.ExecuteAsync("UPDATE Users SET DefaultAccount=@grant WHERE Id=@UserId AND DefaultAccount IS NULL AND (SELECT COUNT(*) FROM AccountsUsers WHERE UserId=@UserId)=1", new { command.UserId, grant }, tx);
        }
        if (command.RevokeAccountId is int revoke)
        {
            await conn.ExecuteAsync("DELETE FROM AccountsUsers WHERE UserId=@UserId AND AccountId=@revoke", new { command.UserId, revoke }, tx);
            await conn.ExecuteAsync("UPDATE Users SET DefaultAccount=NULL WHERE Id=@UserId AND DefaultAccount=@revoke", new { command.UserId, revoke }, tx);
        }
        return Result(UserAdministrationStatus.Success, intern);
    });

    public Task<UserAdministrationResult> DeleteAsync(int actorId, int userId) => ExecuteAsync(actorId, async (conn, tx) =>
    {
        if (actorId == userId) return Result(UserAdministrationStatus.SelfChange);
        var user = await conn.QuerySingleOrDefaultAsync<User>("SELECT Id, AuthLevel FROM Users WHERE Id=@userId FOR UPDATE", new { userId }, tx);
        if (user is null) return Result(UserAdministrationStatus.Success);
        if (user.IsAdmin() && await AdminCountAsync(conn, tx) <= 1) return Result(UserAdministrationStatus.LastAdmin);
        await conn.ExecuteAsync("DELETE FROM Users WHERE Id=@userId", new { userId }, tx);
        return Result(UserAdministrationStatus.Success);
    });

    private async Task<UserAdministrationResult> ExecuteAsync(int actorId, Func<MySqlConnection, MySqlTransaction, Task<UserAdministrationResult>> action)
    {
        guard.ThrowIfUnavailable();
        await using var conn = new MySqlConnection(configuration.GetConnectionString("default") ?? string.Empty);
        await conn.OpenAsync();
        var lockName = await conn.ExecuteScalarAsync<string>("SELECT CONCAT('cya2.users.', LEFT(SHA2(DATABASE(), 256), 40))");
        if (await conn.ExecuteScalarAsync<int?>("SELECT GET_LOCK(@lockName, 10)", new { lockName }) != 1)
            throw new InvalidOperationException("Unable to acquire user administration lock.");
        try
        {
            await using var tx = await conn.BeginTransactionAsync();
            var actorRole = await conn.ExecuteScalarAsync<string?>("SELECT AuthLevel FROM Users WHERE Id=@actorId FOR UPDATE", new { actorId }, tx);
            if (!string.Equals(actorRole?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase)) return Result(UserAdministrationStatus.NotAuthorized);
            try
            {
                var result = await action(conn, tx);
                if (result.Status == UserAdministrationStatus.Success) await tx.CommitAsync();
                else await tx.RollbackAsync();
                return result;
            }
            catch (MySqlException ex) when (ex.Number == 1062 && ex.Message.Contains("uq_users_email", StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync();
                return Result(UserAdministrationStatus.DuplicateEmail);
            }
        }
        finally
        {
            await conn.ExecuteAsync("SELECT RELEASE_LOCK(@lockName)", new { lockName });
        }
    }

    private static Task<int> AdminCountAsync(MySqlConnection conn, MySqlTransaction tx) =>
        conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE TRIM(AuthLevel)='Admin'", transaction: tx);

    private static async Task<bool> AccountsExistAsync(MySqlConnection conn, MySqlTransaction tx, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return true;
        return ids.All(id => id > 0) && await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Accounts WHERE AccountId IN @ids", new { ids }, tx) == ids.Distinct().Count();
    }

    private static Task<Account?> FindAccountAsync(MySqlConnection conn, MySqlTransaction tx, string fund) =>
        conn.QueryFirstOrDefaultAsync<Account>("SELECT AccountId, Fund FROM Accounts WHERE Fund=@fund LIMIT 1 FOR UPDATE", new { fund }, tx);

    private static async Task<UserAdministrationStatus> EnsureInternAsync(MySqlConnection conn, MySqlTransaction tx, int userId, string name, string? oldName, bool reuse)
    {
        var fund = InternAccountUtility.BuildInternFund(name);
        var desired = await FindAccountAsync(conn, tx, fund);
        var linked = (await conn.QueryAsync<Account>("SELECT a.AccountId, a.Fund FROM Accounts a JOIN AccountsUsers au ON au.AccountId=a.AccountId WHERE au.UserId=@userId ORDER BY a.Fund FOR UPDATE", new { userId }, tx)).FirstOrDefault(a => InternAccountUtility.IsInternFund(a.Fund));
        if (desired is not null && desired.AccountId != linked?.AccountId && !reuse) return UserAdministrationStatus.InternReuseRequired;
        if (desired is not null && linked is not null && desired.AccountId != linked.AccountId)
            await conn.ExecuteAsync("DELETE FROM AccountsUsers WHERE UserId=@userId AND AccountId=@id", new { userId, id = linked.AccountId }, tx);
        if (desired is null)
        {
            linked ??= string.IsNullOrWhiteSpace(oldName) ? null : await FindAccountAsync(conn, tx, InternAccountUtility.BuildInternFund(oldName));
            if (linked is not null)
            {
                await conn.ExecuteAsync("UPDATE Accounts SET Fund=@fund WHERE AccountId=@id", new { fund, id = linked.AccountId }, tx);
                desired = linked;
            }
            else
            {
                var id = await conn.ExecuteScalarAsync<int>("INSERT INTO Accounts (Fund, AccountingClass, AccountNumber, CreatedAt, Overhead, SoftCredit, BalanceAdjustment) VALUES (@fund, NULL, @number, UTC_TIMESTAMP(), 12, '', 0); SELECT LAST_INSERT_ID();", new { fund, number = $"INT-{userId}" }, tx);
                desired = new Account { AccountId = id };
            }
        }
        await conn.ExecuteAsync("INSERT INTO AccountsUsers (UserId, AccountId) VALUES (@userId, @id) ON DUPLICATE KEY UPDATE AccountId=VALUES(AccountId)", new { userId, id = desired.AccountId }, tx);
        await conn.ExecuteAsync("UPDATE Users SET DefaultAccount=@id WHERE Id=@userId", new { userId, id = desired.AccountId }, tx);
        return UserAdministrationStatus.Success;
    }
}
