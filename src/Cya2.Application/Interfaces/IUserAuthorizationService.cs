namespace Cya2.Application.Interfaces;

public interface IUserAuthorizationService
{
    Task<UserAuthorizationResult> ValidateUserAsync(string userIdentifier);
    Task<UserAuthorizationResult> ValidateAccountAccessAsync(string userIdentifier, int accountId);
    Task<UserAuthorizationResult> ValidateAccountAccessAsync(string userIdentifier, string accountFund);
}

public sealed record UserAuthorizationResult(
    bool IsAuthorized,
    bool UserExists,
    bool IsAdmin,
    bool IsViewer,
    int UserId,
    string Reason)
{
    public bool CanAccessAllAccounts => IsAdmin || IsViewer;

    public static UserAuthorizationResult Denied(string reason) =>
        new(false, false, false, false, 0, reason);
}
