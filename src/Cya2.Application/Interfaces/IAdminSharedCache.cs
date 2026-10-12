using Cya2.Application.DTOs;
using Cya2.Core.Entities;

namespace Cya2.Application.Interfaces;

public interface IAdminSharedCache
{
    bool TryGetAccounts(long generation, out IReadOnlyList<Account> accounts);
    void SetAccounts(long generation, IReadOnlyList<Account> accounts);

    bool TryGetSubAccounts(long generation, out IReadOnlyList<SubAccount> subAccounts);
    void SetSubAccounts(long generation, IReadOnlyList<SubAccount> subAccounts);

    bool TryGetStaff(long generation, out IReadOnlyList<AdminUserDto> staff);
    void SetStaff(long generation, IReadOnlyList<AdminUserDto> staff);

    bool TryGetOverview(long generation, DateTime startDate, DateTime endDate, out IReadOnlyList<AdminAccountOverviewDto> overview);
    void SetOverview(long generation, DateTime startDate, DateTime endDate, IReadOnlyList<AdminAccountOverviewDto> overview);

    void InvalidateAll();
}
