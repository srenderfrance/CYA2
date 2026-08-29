using Cya2.Core.Entities;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Interfaces;

public interface IAccountSnapshotWarmupService
{
    void WarmDefaultAccount(Account account);
    Task WarmInitialAccountsAsync(IEnumerable<UserAccountContextAccount> accounts, int? defaultAccountId, string userId = "", bool isAdminOrViewer = false, DateRange? donorSummaryRange = null);
    void RecordSelection(Account account);
    void RecordSelection(UserAccountContextAccount account);
    void RecordSelection(UserAccountContextAccount account, string userId, bool isAdminOrViewer = false);
    Task WarmSelectedAccountAsync(UserAccountContextAccount account, string userId, bool isAdminOrViewer = false, DateRange? donorSummaryRange = null);
    void Invalidate();
}
