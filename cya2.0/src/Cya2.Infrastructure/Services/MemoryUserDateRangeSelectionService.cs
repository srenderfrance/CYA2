using Cya2.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Cya2.Infrastructure.Services;

public sealed class MemoryUserDateRangeSelectionService : IUserDateRangeSelectionService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryUserDateRangeSelectionService> _logger;
    private readonly TimeSpan _defaultTtl = TimeSpan.FromHours(24);

    public MemoryUserDateRangeSelectionService(IMemoryCache cache, ILogger<MemoryUserDateRangeSelectionService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public void SetDateRange(string userId, DateTime startDate, DateTime endDate, string preset, TimeSpan? ttl = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var key = UserKey(userId);
        var normalizedPreset = string.IsNullOrWhiteSpace(preset) ? "ThisMonth" : preset;
        if (_cache.TryGetValue(key, out UserDateRangeSelection? existing)
            && existing != null
            && existing.StartDate.Date == startDate.Date
            && existing.EndDate.Date == endDate.Date
            && string.Equals(existing.Preset, normalizedPreset, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("MemoryUserDateRangeSelectionService: Range unchanged for {UserId}; skipping write", userId);
            return;
        }

        var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl ?? _defaultTtl };
        var selection = new UserDateRangeSelection
        {
            StartDate = startDate,
            EndDate = endDate,
            Preset = normalizedPreset
        };

        _cache.Set(key, selection, options);
        _logger.LogDebug("MemoryUserDateRangeSelectionService: Set range for {UserId} -> {Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, preset={Preset}", userId, startDate, endDate, selection.Preset);
    }

    public bool TryGetDateRange(string userId, out UserDateRangeSelection selection)
    {
        selection = new UserDateRangeSelection();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var found = _cache.TryGetValue(UserKey(userId), out UserDateRangeSelection? cached) && cached != null;
        if (found)
        {
            selection = cached!;
        }

        _logger.LogDebug("MemoryUserDateRangeSelectionService: TryGet for {UserId} -> found={Found}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, preset={Preset}", userId, found, selection.StartDate, selection.EndDate, selection.Preset);
        return found;
    }

    public void RemoveDateRange(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        _cache.Remove(UserKey(userId));
        _logger.LogDebug("MemoryUserDateRangeSelectionService: Removed range for {UserId}", userId);
    }

    private static string UserKey(string userId) => $"UserDateRange:{userId}";
}
