using Cya2.Core.Entities;

namespace Cya2.Application.Interfaces;

public interface IAdminRecentAccountSnapshotService
{
    void WarmDefaultAccount(Account account);
    void RecordSelection(Account account);
    void Invalidate();
}
