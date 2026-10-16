using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

/// <summary>
/// Invalidates all shared caches after a DB import or rollback so stale
/// data is never served to the UI.
/// </summary>
public sealed class ImportCacheInvalidator : IImportCacheInvalidator
{
    private readonly IDonorSummaryCache _donorCache;
    private readonly IMissingGiftCache _missingGiftCache;
    private readonly IDonationDataCache _donationDataCache;
    private readonly IAccountingDataCache _accountingDataCache;
    private readonly IUserDonationDataCache _userDonationDataCache;
    private readonly IUserAccountingDataCache _userAccountingDataCache;
    private readonly IAdminSharedCache _adminSharedCache;
    private readonly IAccountMetadataCache _accountMetadataCache;
    private readonly IFundReferenceCache _fundReferenceCache;
    private readonly IDonorContactCache _donorContactCache;
    private readonly IAccountBalanceCache _accountBalanceCache;
    private readonly IHomeSummaryCache _homeSummaryCache;
    private readonly IAdminCacheWarmupCoordinator _adminCacheWarmupCoordinator;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
    private readonly ILogger<ImportCacheInvalidator> _logger;

    public ImportCacheInvalidator(
        IDonorSummaryCache donorCache,
        IMissingGiftCache missingGiftCache,
        IDonationDataCache donationDataCache,
        IAccountingDataCache accountingDataCache,
        IUserDonationDataCache userDonationDataCache,
        IUserAccountingDataCache userAccountingDataCache,
        IAdminSharedCache adminSharedCache,
        IAccountMetadataCache accountMetadataCache,
        IFundReferenceCache fundReferenceCache,
        IDonorContactCache donorContactCache,
        IAccountBalanceCache accountBalanceCache,
        IHomeSummaryCache homeSummaryCache,
        IAdminCacheWarmupCoordinator adminCacheWarmupCoordinator,
        ICacheInvalidationVersion cacheInvalidationVersion,
        ILogger<ImportCacheInvalidator> logger)
    {
        _donorCache    = donorCache;
        _missingGiftCache = missingGiftCache;
        _donationDataCache = donationDataCache;
        _accountingDataCache = accountingDataCache;
        _userDonationDataCache = userDonationDataCache;
        _userAccountingDataCache = userAccountingDataCache;
        _adminSharedCache = adminSharedCache;
        _accountMetadataCache = accountMetadataCache;
        _fundReferenceCache = fundReferenceCache;
        _donorContactCache = donorContactCache;
        _accountBalanceCache = accountBalanceCache;
        _homeSummaryCache = homeSummaryCache;
        _adminCacheWarmupCoordinator = adminCacheWarmupCoordinator;
        _cacheInvalidationVersion = cacheInvalidationVersion;
        _logger        = logger;
    }

    public void InvalidateAll()
    {
        Invalidate(CacheDataDomains.All);
        _adminCacheWarmupCoordinator.QueueWarmup();
        _logger.LogInformation("All shared caches cleared after import/rollback");
    }

    public void Invalidate(CacheDataDomains domains)
    {
        if (domains == CacheDataDomains.None)
        {
            return;
        }

        var hasDonations = domains.HasFlag(CacheDataDomains.Donations);
        var hasAccounting = domains.HasFlag(CacheDataDomains.Accounting);
        var hasAccountMetadata = domains.HasFlag(CacheDataDomains.AccountMetadata);
        var hasDonorContacts = domains.HasFlag(CacheDataDomains.DonorContacts);
        var hasAuthorization = domains.HasFlag(CacheDataDomains.Authorization);
        if (hasDonations)
        {
            _userDonationDataCache.RemoveStaleGenerations(_cacheInvalidationVersion.Donations);
            _donorCache.InvalidateAll();
            _missingGiftCache.InvalidateAll();
        }

        if (hasAccounting)
        {
            _userAccountingDataCache.RemoveStaleGenerations(_cacheInvalidationVersion.Accounting);
        }

        if (hasDonorContacts)
        {
            if (!hasDonations)
            {
                _donorCache.InvalidateAll();
                _missingGiftCache.InvalidateAll();
            }
            _donorContactCache.InvalidateAll();
        }

        if (hasAuthorization && !hasDonations && !hasDonorContacts)
        {
            _donorCache.InvalidateAll();
            _missingGiftCache.InvalidateAll();
        }

        _cacheInvalidationVersion.Invalidate(domains);

        if (domains == CacheDataDomains.All)
        {
            _donationDataCache.InvalidateAll();
            _accountingDataCache.InvalidateAll();
            _accountBalanceCache.InvalidateAll();
            _homeSummaryCache.InvalidateAll();
        }
        else if (hasDonations || hasAccounting || hasAccountMetadata)
        {
            if (hasDonations || hasAccountMetadata)
                _donationDataCache.RemoveStaleGenerations(_cacheInvalidationVersion.Donations);
            if (hasAccounting || hasAccountMetadata)
                _accountingDataCache.RemoveStaleGenerations(_cacheInvalidationVersion.Accounting);
            _accountBalanceCache.RemoveStaleGeneration(_cacheInvalidationVersion.Current);
            _homeSummaryCache.RemoveStaleGenerations(_cacheInvalidationVersion.GetGeneration(
                CacheDataDomains.Donations | CacheDataDomains.Accounting | CacheDataDomains.AccountMetadata));
        }

        if (hasAccountMetadata)
        {
            _adminSharedCache.InvalidateAll();
            _accountMetadataCache.InvalidateAll();
        }

        // Keep selector/reference options coherent for metadata and auth updates.
        if (hasAccountMetadata || hasAuthorization)
        {
            _fundReferenceCache.InvalidateAll();
        }

        if (hasDonations || hasAccounting || hasAccountMetadata)
        {
            _adminCacheWarmupCoordinator.QueueWarmup();
        }

        _logger.LogInformation("Shared caches invalidated for domains {Domains}", domains);
    }
}
