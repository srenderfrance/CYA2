namespace Cya2.Application.Interfaces;

public interface IHomeAuthorizationService
{
    Task<HomeAuthorizationResult> AuthorizeAsync(
        string userIdentifier,
        string? preferredFund,
        CancellationToken cancellationToken = default);
}

public sealed record HomeAuthorizationResult(
    bool IsAuthorized,
    bool IsViewer,
    int UserId,
    UserAccountContext? Context,
    UserAccountContextAccount? SelectedAccount,
    string Reason,
    bool IsRequestedAccountAccessDenied = false)
{
    public bool HasAccounts => Context?.Accounts.Count > 0;

    public static HomeAuthorizationResult Denied(string reason) =>
        new(false, false, 0, null, null, reason);
}
