using Cya2.Application.Interfaces;

namespace Cya2.Application.Services;

public class SessionUserStateService : ISessionUserStateService
{
    private readonly IUserScopedCacheInvalidator _overflowCacheInvalidator;

    public SessionUserStateService(IUserScopedCacheInvalidator overflowCacheInvalidator)
    {
        _overflowCacheInvalidator = overflowCacheInvalidator;
    }

    public string CurrentUserId { get; set; } = string.Empty;
    public string SelectedAccountFund { get; set; } = string.Empty;
    public string DefaultAccountFund { get; set; } = string.Empty;

    public DateTime? SelectedStartDate { get; set; }
    public DateTime? SelectedEndDate { get; set; }
    public string SelectedDatePreset { get; set; } = "ThisMonth";

    public void ResetForUser(string userId)
    {
        if (string.Equals(CurrentUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _overflowCacheInvalidator.RemoveUser(CurrentUserId);

        CurrentUserId = userId;
        SelectedAccountFund = string.Empty;
        DefaultAccountFund = string.Empty;
        SelectedStartDate = null;
        SelectedEndDate = null;
        SelectedDatePreset = "ThisMonth";

        // No server-side selection store available in this refactor; nothing to remove here.
    }
}
