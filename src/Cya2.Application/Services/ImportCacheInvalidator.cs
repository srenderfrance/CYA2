using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

/// <summary>
/// Invalidates all session caches after a DB import or rollback so stale
/// data is never served to the UI.
/// </summary>
public sealed class ImportCacheInvalidator : IImportCacheInvalidator
{
    private readonly ISessionDonationDataCacheService _donationCache;
    private readonly ISessionDonorSummaryCacheService _donorCache;
    private readonly ISessionMissingGiftCacheService _missingGiftCache;
    private readonly ISessionExpenseDataCacheService _expenseCache;
    private readonly ISessionDashboardDtoCacheService _dashboardCache;
    private readonly IAccountSnapshotCache _accountSnapshotCache;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
    private readonly ILogger<ImportCacheInvalidator> _logger;

    public ImportCacheInvalidator(
        ISessionDonationDataCacheService donationCache,
        ISessionDonorSummaryCacheService donorCache,
        ISessionMissingGiftCacheService missingGiftCache,
        ISessionExpenseDataCacheService expenseCache,
        ISessionDashboardDtoCacheService dashboardCache,
        IAccountSnapshotCache accountSnapshotCache,
        ICacheInvalidationVersion cacheInvalidationVersion,
        ILogger<ImportCacheInvalidator> logger)
    {
        _donationCache = donationCache;
        _donorCache    = donorCache;
        _missingGiftCache = missingGiftCache;
        _expenseCache  = expenseCache;
        _dashboardCache = dashboardCache;
        _accountSnapshotCache = accountSnapshotCache;
        _cacheInvalidationVersion = cacheInvalidationVersion;
        _logger        = logger;
    }

    public void InvalidateAll()
    {
        _donationCache.InvalidateAll();
        _donorCache.InvalidateAll();
        _missingGiftCache.InvalidateAll();
        _expenseCache.InvalidateAll();
        _dashboardCache.InvalidateAll();
        _accountSnapshotCache.InvalidateAll();
        _cacheInvalidationVersion.Invalidate();
        _logger.LogInformation("All session and account snapshot caches cleared after import/rollback");
    }
}
