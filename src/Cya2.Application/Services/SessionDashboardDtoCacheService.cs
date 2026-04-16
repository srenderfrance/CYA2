using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Cya2.Application.Services;

public class SessionDashboardDtoCacheService : ISessionDashboardDtoCacheService
{
    private const int MaxItemsPerUser = 15;
    private readonly ILogger<SessionDashboardDtoCacheService> _logger;

    private sealed class UserDashboardCache
    {
        public Dictionary<string, FinancialDashboardDto> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
        public LinkedList<string> LruFunds { get; } = new();
    }

    private readonly Dictionary<string, UserDashboardCache> _cacheByUser = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public SessionDashboardDtoCacheService(ILogger<SessionDashboardDtoCacheService> logger)
    {
        _logger = logger;
    }

    public bool TryGetDashboard(string userId, string fund, out FinancialDashboardDto dashboard)
    {
        var sw = Stopwatch.StartNew();
        dashboard = default!;

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fund))
        {
            return false;
        }

        lock (_sync)
        {
            if (!_cacheByUser.TryGetValue(userId, out var userCache))
            {
                _logger.LogInformation("Dashboard DTO cache miss: user={UserId}, fund={Fund}, reason=missing-user-cache, elapsedMs={ElapsedMs}", userId, fund, sw.ElapsedMilliseconds);
                return false;
            }

            if (!userCache.Items.TryGetValue(fund, out var dto))
            {
                _logger.LogInformation("Dashboard DTO cache miss: user={UserId}, fund={Fund}, reason=missing-fund, cachedFunds={CachedFunds}, elapsedMs={ElapsedMs}", userId, fund, userCache.Items.Count, sw.ElapsedMilliseconds);
                return false;
            }

            dashboard = dto;
            Touch(userCache, fund);
            _logger.LogInformation(
                "Dashboard DTO cache hit: user={UserId}, fund={Fund}, accounts={Accounts}, selectedDonationsRows={SelectedRows}, defaultDonationsRows={DefaultRows}, approxBytes={ApproxBytes}, elapsedMs={ElapsedMs}",
                userId,
                fund,
                dashboard.UserAccounts?.Count ?? 0,
                dashboard.SelectedAccountDonations?.Donations?.Count ?? 0,
                dashboard.DefaultAccountDonations?.Donations?.Count ?? 0,
                EstimateBytes(dashboard),
                sw.ElapsedMilliseconds);
            return true;
        }
    }

    public IReadOnlyCollection<string> GetFunds(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<string>();
        }

        lock (_sync)
        {
            if (!_cacheByUser.TryGetValue(userId, out var userCache))
            {
                return Array.Empty<string>();
            }

            return userCache.LruFunds.ToList();
        }
    }

    public void SetDashboard(string userId, string fund, FinancialDashboardDto dashboard, bool prioritize = false)
    {
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fund) || dashboard == null)
        {
            return;
        }

        lock (_sync)
        {
            if (!_cacheByUser.TryGetValue(userId, out var userCache))
            {
                userCache = new UserDashboardCache();
                _cacheByUser[userId] = userCache;
            }

            userCache.Items[fund] = dashboard;
            Touch(userCache, fund, prioritize);

            while (userCache.Items.Count > MaxItemsPerUser)
            {
                var evictFund = userCache.LruFunds.Last?.Value;
                if (string.IsNullOrWhiteSpace(evictFund))
                {
                    break;
                }

                userCache.LruFunds.RemoveLast();
                userCache.Items.Remove(evictFund);
            }

            _logger.LogInformation(
                "Dashboard DTO cache set: user={UserId}, fund={Fund}, accounts={Accounts}, selectedDonationsRows={SelectedRows}, defaultDonationsRows={DefaultRows}, approxBytes={ApproxBytes}, cachedFunds={CachedFunds}, prioritize={Prioritize}, elapsedMs={ElapsedMs}",
                userId,
                fund,
                dashboard.UserAccounts?.Count ?? 0,
                dashboard.SelectedAccountDonations?.Donations?.Count ?? 0,
                dashboard.DefaultAccountDonations?.Donations?.Count ?? 0,
                EstimateBytes(dashboard),
                userCache.Items.Count,
                prioritize,
                sw.ElapsedMilliseconds);
        }
    }

    public void InvalidateAll()
    {
        lock (_sync) { _cacheByUser.Clear(); }
        _logger.LogInformation("Dashboard DTO cache invalidated (import/rollback)");
    }

    private static void Touch(UserDashboardCache userCache, string fund, bool prioritize = true)
    {
        var node = userCache.LruFunds.Find(fund);
        if (node != null)
        {
            userCache.LruFunds.Remove(node);
        }

        if (prioritize)
        {
            userCache.LruFunds.AddFirst(fund);
        }
        else
        {
            userCache.LruFunds.AddLast(fund);
        }
    }

    private static long EstimateBytes(FinancialDashboardDto dto)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(dto).LongLength;
        }
        catch
        {
            return 0;
        }
    }
}
