using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public class SessionExpenseDataCacheService : ISessionExpenseDataCacheService
{
    private readonly Dictionary<string, ExpenseDataDto> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly ILogger<SessionExpenseDataCacheService> _logger;

    public SessionExpenseDataCacheService(ILogger<SessionExpenseDataCacheService> logger)
    {
        _logger = logger;
    }

    public bool TryGetExpenseData(string userId, string fund, DateTime startDate, DateTime endDate, out ExpenseDataDto data)
    {
        data = default!;
        var key = BuildKey(userId, fund, startDate, endDate);
        lock (_sync)
        {
            if (!_cache.TryGetValue(key, out var cached))
            {
                _logger.LogInformation("Expense DTO cache miss: user={UserId}, fund={Fund}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}", userId, fund, startDate, endDate);
                return false;
            }

            data = cached;
            _logger.LogInformation("Expense DTO cache hit: user={UserId}, fund={Fund}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, expenses={ExpenseCount}, transfers={TransferCount}", userId, fund, startDate, endDate, cached.ExpenseTransactions?.Count ?? 0, cached.TransferTransactions?.Count ?? 0);
            return true;
        }
    }

    public void SetExpenseData(string userId, string fund, DateTime startDate, DateTime endDate, ExpenseDataDto data)
    {
        var key = BuildKey(userId, fund, startDate, endDate);
        lock (_sync)
        {
            _cache[key] = data;
        }

        _logger.LogInformation("Expense DTO cache set: user={UserId}, fund={Fund}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, expenses={ExpenseCount}, transfers={TransferCount}", userId, fund, startDate, endDate, data.ExpenseTransactions?.Count ?? 0, data.TransferTransactions?.Count ?? 0);
    }

    public void InvalidateAll()
    {
        lock (_sync) { _cache.Clear(); }
        _logger.LogInformation("Expense data cache invalidated (import/rollback)");
    }

    private static string BuildKey(string userId, string fund, DateTime startDate, DateTime endDate)
        => $"{userId}|{fund}|{startDate:yyyyMMdd}|{endDate:yyyyMMdd}";
}
