using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.Utilities;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Cya2.Application.Interfaces;
using Cya2.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cya2.Infrastructure.Repositories;

public sealed class UserAdministrationStore(IConfiguration configuration, IDatabaseGuard guard, ILogger<UserAdministrationStore>? logger = null) : IUserAdministrationStore, IUserBatchAdministrationStore
{
    private readonly ILogger _logger = logger ?? NullLogger<UserAdministrationStore>.Instance;
    private static UserAdministrationResult Result(UserAdministrationStatus status, bool intern = false) => new(status, intern);

    public Task<UserAdministrationResult> CreateBatchAsync(int actorId, IReadOnlyList<ImportUserEntry> users) => ExecuteAsync(actorId, async (conn, tx) =>
    {
        foreach (var user in users)
        {
            if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Email=@Email", user, tx) != 0)
                return Result(UserAdministrationStatus.DuplicateEmail);
            var id = await conn.ExecuteScalarAsync<int>("INSERT INTO Users (GoogleId, Email, Name, Language, AuthLevel) VALUES (NULL, @Email, @Name, 'en-US', @AuthLevel); SELECT LAST_INSERT_ID();", user, tx);
            using var audit = AuditOperationScope.Begin(actorId, targetUserId: id);
            await TransactionalAudit.RecordAsync(conn, tx, AuditEventTypes.AdminUserCreated, _logger, targetUserId: id);
        }
        return Result(UserAdministrationStatus.Success);
    });

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
        await TransactionalAudit.RecordAsync(conn, tx, AuditEventTypes.AdminUserCreated, _logger, targetUserId: id);
        if (intern || command.AccountIds.Count > 0)
            await TransactionalAudit.RecordAsync(conn, tx, AuditEventTypes.AccountAccessGranted, _logger, itemCount: intern ? 1 : command.AccountIds.Distinct().Count(), targetUserId: id);
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
        if (user.IsIntern()) role = user.AuthLevel;
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
        if (command.GrantAccountId is int grantId && await HasAccessAsync(conn, tx, command.UserId, grantId))
            return Result(UserAdministrationStatus.AccountAlreadyLinked);
        if (command.RevokeAccountId is int revokeId && !await HasAccessAsync(conn, tx, command.UserId, revokeId))
            return Result(UserAdministrationStatus.AccountNotLinked);

        var intern = string.Equals(role, "Intern", StringComparison.OrdinalIgnoreCase);
        if (intern && (!string.IsNullOrWhiteSpace(command.Name) || !string.IsNullOrWhiteSpace(command.Email) || !string.IsNullOrWhiteSpace(command.AuthLevel)))
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
            await conn.ExecuteAsync("UPDATE Users SET DefaultAccount=(SELECT CASE WHEN COUNT(*)=1 THEN MIN(AccountId) ELSE NULL END FROM AccountsUsers WHERE UserId=@UserId) WHERE Id=@UserId AND DefaultAccount=@revoke", new { command.UserId, revoke }, tx);
        }
        if (!string.IsNullOrWhiteSpace(command.Name) || !string.IsNullOrWhiteSpace(command.Email) || !string.IsNullOrWhiteSpace(command.AuthLevel))
            await TransactionalAudit.RecordAsync(conn, tx, AuditEventTypes.AdminUserUpdated, _logger, targetUserId: command.UserId);
        if (command.GrantAccountId.HasValue)
            await TransactionalAudit.RecordAsync(conn, tx, AuditEventTypes.AccountAccessGranted, _logger, itemCount: 1, targetUserId: command.UserId);
        if (command.RevokeAccountId.HasValue)
            await TransactionalAudit.RecordAsync(conn, tx, AuditEventTypes.AccountAccessRevoked, _logger, itemCount: 1, targetUserId: command.UserId);
        return Result(UserAdministrationStatus.Success, intern);
    });

    public Task<UserAdministrationResult> DeleteAsync(int actorId, int userId) => ExecuteAsync(actorId, async (conn, tx) =>
    {
        if (actorId == userId) return Result(UserAdministrationStatus.SelfChange);
        var user = await conn.QuerySingleOrDefaultAsync<User>("SELECT Id, AuthLevel FROM Users WHERE Id=@userId FOR UPDATE", new { userId }, tx);
        if (user is null) return Result(UserAdministrationStatus.NotFound);
        if (user.IsAdmin() && await AdminCountAsync(conn, tx) <= 1) return Result(UserAdministrationStatus.LastAdmin);
        await conn.ExecuteAsync("DELETE FROM Users WHERE Id=@userId", new { userId }, tx);
        await TransactionalAudit.RecordAsync(conn, tx, AuditEventTypes.AdminUserDeleted, _logger, targetUserId: userId);
        return Result(UserAdministrationStatus.Success);
    });

    private async Task<UserAdministrationResult> ExecuteAsync(int actorId, Func<MySqlConnection, MySqlTransaction, Task<UserAdministrationResult>> action)
    {
        using var audit = AuditOperationScope.Begin(actorId, AuditOperationScope.Current?.OperationId);
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
        conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE LOWER(TRIM(AuthLevel))='admin'", transaction: tx);

    private static async Task<bool> HasAccessAsync(MySqlConnection conn, MySqlTransaction tx, int userId, int accountId) =>
        await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AccountsUsers WHERE UserId=@userId AND AccountId=@accountId", new { userId, accountId }, tx) != 0;

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
