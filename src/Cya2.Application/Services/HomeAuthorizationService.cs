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

        var selectedAccount = _accountContextService.ResolveSelectedAccount(
            context,
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
                "User has no accessible accounts.");
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
