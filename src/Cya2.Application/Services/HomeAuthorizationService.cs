using Cya2.Application.Interfaces;

namespace Cya2.Application.Services;

public sealed class HomeAuthorizationService : IHomeAuthorizationService
{
    private readonly IUserAccountContextService _accountContextService;

    public HomeAuthorizationService(IUserAccountContextService accountContextService)
    {
        _accountContextService = accountContextService;
    }

    public async Task<HomeAuthorizationResult> AuthorizeAsync(
        string userIdentifier,
        string? preferredFund,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = await _accountContextService.GetContextAsync(userIdentifier);
        cancellationToken.ThrowIfCancellationRequested();

        if (context is null)
        {
            return HomeAuthorizationResult.Denied("User does not exist or could not be resolved.");
        }

        var hasRequestedAccount = !context.IsIntern && !string.IsNullOrWhiteSpace(preferredFund);
        var selectedAccount = hasRequestedAccount
            ? context.Accounts.FirstOrDefault(account =>
                string.Equals(account.Fund, preferredFund, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(account.InternName, preferredFund, StringComparison.OrdinalIgnoreCase))
            : _accountContextService.ResolveSelectedAccount(
                context,
                // Intern home views select by intern identity because the fund is not a unique donation-data key.
                context.IsIntern ? context.InternName : preferredFund);
        if (selectedAccount is null && context.IsIntern)
        {
            return new HomeAuthorizationResult(
                true,
                false,
                context.UserId,
                context,
                null,
                string.Empty);
        }

        if (selectedAccount is null)
        {
            return new HomeAuthorizationResult(
                false,
                context.IsViewer,
                context.UserId,
                context,
                null,
                context.Accounts.Count == 0
                    ? "User has no accessible accounts."
                    : "User does not have access to the requested account.",
                IsRequestedAccountAccessDenied: context.Accounts.Count > 0 && hasRequestedAccount);
        }

        return new HomeAuthorizationResult(
            true,
            context.IsViewer,
            context.UserId,
            context,
            selectedAccount,
            string.Empty);
    }
}
