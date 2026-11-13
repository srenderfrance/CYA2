namespace Cya2.Core.Interfaces;

public enum UserAdministrationStatus
{
    Success,
    NotAuthorized,
    NotFound,
    SelfChange,
    LastAdmin,
    DuplicateEmail,
    InternRoleChange,
    InternReuseRequired,
    InvalidAccount,
    AccountAlreadyLinked,
    AccountNotLinked
}

public sealed record UserAdministrationResult(UserAdministrationStatus Status, bool InternAccountChanged = false);

public sealed record CreateUserCommand(int ActorId, string Name, string Email, string AuthLevel, IReadOnlyList<int> AccountIds, bool ReuseInternAccount);

public sealed record UpdateUserCommand(int ActorId, int UserId, string? Name, string? Email, string? AuthLevel, int? GrantAccountId, int? RevokeAccountId, bool ReuseInternAccount);

public sealed record ImportUserEntry(string Name, string Email, string AuthLevel);

public interface IUserBatchAdministrationStore
{
    Task<UserAdministrationResult> CreateBatchAsync(int actorId, IReadOnlyList<ImportUserEntry> users);
}

public interface IUserAdministrationStore
{
    Task<UserAdministrationResult> CreateAsync(CreateUserCommand command);
    Task<UserAdministrationResult> UpdateAsync(UpdateUserCommand command);
    Task<UserAdministrationResult> DeleteAsync(int actorId, int userId);
}
