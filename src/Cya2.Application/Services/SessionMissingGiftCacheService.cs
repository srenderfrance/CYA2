using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class SessionMissingGiftCacheService : ISessionMissingGiftCacheService
{
    private readonly Dictionary<string, List<DonorSummaryDto>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly ILogger<SessionMissingGiftCacheService> _logger;

    public SessionMissingGiftCacheService(ILogger<SessionMissingGiftCacheService> logger)
    {
        _logger = logger;
    }

    public bool TryGetMissingGiftDonors(
        int accountId,
        string fund,
        DateTime startDate,
        DateTime endDate,
        out List<DonorSummaryDto> data)
    {
        var key = BuildKey(accountId, fund, startDate, endDate);
        lock (_sync)
        {
            if (!_cache.TryGetValue(key, out var cached))
            {
                data = [];
                return false;
            }

            data = cached.ToList();
            return true;
        }
    }

    public void SetMissingGiftDonors(
        int accountId,
        string fund,
        DateTime startDate,
        DateTime endDate,
        List<DonorSummaryDto> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var key = BuildKey(accountId, fund, startDate, endDate);
        lock (_sync)
        {
            _cache[key] = data.ToList();
        }
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _cache.Clear();
        }

        _logger.LogInformation("Missing-gift warning cache invalidated (import/rollback)");
    }

    private static string BuildKey(int accountId, string fund, DateTime startDate, DateTime endDate)
        => $"{accountId}|{Normalize(fund)}|{startDate:yyyyMMdd}|{endDate:yyyyMMdd}";

    private static string Normalize(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
