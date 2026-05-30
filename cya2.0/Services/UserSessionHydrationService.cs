using System.Security.Claims;
using Cya2.Application.Interfaces;

namespace cya2.Services;

public sealed class UserSessionHydrationService
{
    private readonly IUserIdResolver _userIdResolver;
    private readonly ISessionUserStateService _sessionUserStateService;
    private readonly IUserSelectionService _userSelectionService;
    private readonly IUserDateRangeSelectionService _dateRangeSelectionService;

    public UserSessionHydrationService(
        IUserIdResolver userIdResolver,
        ISessionUserStateService sessionUserStateService,
        IUserSelectionService userSelectionService,
        IUserDateRangeSelectionService dateRangeSelectionService)
    {
        _userIdResolver = userIdResolver;
        _sessionUserStateService = sessionUserStateService;
        _userSelectionService = userSelectionService;
        _dateRangeSelectionService = dateRangeSelectionService;
    }

    public string HydrateForUser(
        ClaimsPrincipal user,
        bool hydrateSelectedAccount = true,
        bool hydrateDateRange = true,
        bool resetWhenUserChanges = true)
    {
        var resolvedUserId = _userIdResolver.ResolveUserId(user, _sessionUserStateService.CurrentUserId);

        if (resetWhenUserChanges && !string.Equals(_sessionUserStateService.CurrentUserId, resolvedUserId, StringComparison.OrdinalIgnoreCase))
        {
            _sessionUserStateService.ResetForUser(resolvedUserId);
        }
        else if (string.IsNullOrWhiteSpace(_sessionUserStateService.CurrentUserId))
        {
            _sessionUserStateService.CurrentUserId = resolvedUserId;
        }

        if (hydrateSelectedAccount
            && string.IsNullOrWhiteSpace(_sessionUserStateService.SelectedAccountFund)
            && !string.IsNullOrWhiteSpace(resolvedUserId)
            && _userSelectionService.TryGetSelectedAccount(resolvedUserId, out var hydratedAccount)
            && !string.IsNullOrWhiteSpace(hydratedAccount))
        {
            _sessionUserStateService.SelectedAccountFund = hydratedAccount;
        }

        if (hydrateDateRange
            && (!_sessionUserStateService.SelectedStartDate.HasValue || !_sessionUserStateService.SelectedEndDate.HasValue)
            && !string.IsNullOrWhiteSpace(resolvedUserId)
            && _dateRangeSelectionService.TryGetDateRange(resolvedUserId, out var hydratedRange))
        {
            _sessionUserStateService.SelectedStartDate = hydratedRange.StartDate;
            _sessionUserStateService.SelectedEndDate = hydratedRange.EndDate;
            _sessionUserStateService.SelectedDatePreset = string.IsNullOrWhiteSpace(hydratedRange.Preset) ? "ThisMonth" : hydratedRange.Preset;
        }

        return resolvedUserId;
    }
}
