using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface ISessionExpenseDataCacheService
{
    bool TryGetExpenseData(string userId, string fund, DateTime startDate, DateTime endDate, out ExpenseDataDto data);
    void SetExpenseData(string userId, string fund, DateTime startDate, DateTime endDate, ExpenseDataDto data);
    void InvalidateAll();
}
