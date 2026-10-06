using Cya2.Application.Interfaces;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Services;

public sealed class AccountSelectionCoordinator : IAccountSelectionCoordinator
{
    private readonly ISessionUserStateService _sessionUserState;
    private readonly IUserSelectionService _userSelectionService;
    private readonly IAccountSnapshotWarmupService _warmupService;
    private readonly IUserAuthorizationService _authorizationService;

    public AccountSelectionCoordinator(
        ISessionUserStateService sessionUserState,
        IUserSelectionService userSelectionService,
        IAccountSnapshotWarmupService warmupService,
        IUserAuthorizationService authorizationService)
    {
        _sessionUserState = sessionUserState;
        _userSelectionService = userSelectionService;
        _warmupService = warmupService;
        _authorizationService = authorizationService;
    }

    public async Task SelectAsync(
        UserAccountContextAccount account,
        string userId,
        bool isAdminOrViewer,
        bool persistSelection,
        DateRange? donorSummaryRange = null)
    {
        var authorization = await _authorizationService.ValidateAccountAccessAsync(userId, account.AccountId);
        if (!authorization.IsAuthorized)
        {
            throw new UnauthorizedAccessException(authorization.Reason);
        }

        _sessionUserState.SelectedAccountFund = account.Fund ?? string.Empty;
        if (persistSelection && !string.IsNullOrWhiteSpace(userId))
        {
            _userSelectionService.SetSelectedAccount(userId, account.Fund ?? string.Empty);
        }

        await _warmupService.WarmSelectedAccountAsync(
            account,
            userId,
            isAdminOrViewer,
            donorSummaryRange);
    }
}
