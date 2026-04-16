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
    private readonly ISessionExpenseDataCacheService _expenseCache;
    private readonly ISessionDashboardDtoCacheService _dashboardCache;
    private readonly ILogger<ImportCacheInvalidator> _logger;

    public ImportCacheInvalidator(
        ISessionDonationDataCacheService donationCache,
        ISessionDonorSummaryCacheService donorCache,
        ISessionExpenseDataCacheService expenseCache,
        ISessionDashboardDtoCacheService dashboardCache,
        ILogger<ImportCacheInvalidator> logger)
    {
        _donationCache = donationCache;
        _donorCache    = donorCache;
        _expenseCache  = expenseCache;
        _dashboardCache = dashboardCache;
        _logger        = logger;
    }

    public void InvalidateAll()
    {
        _donationCache.InvalidateAll();
        _donorCache.InvalidateAll();
        _expenseCache.InvalidateAll();
        _dashboardCache.InvalidateAll();
        _logger.LogInformation("All session caches cleared after import/rollback");
    }
}
