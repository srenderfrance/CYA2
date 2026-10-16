using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.Interfaces;
using Cya2.Core.Utilities;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Services;

public sealed class DonationDataCacheLoader : IDonationDataCacheLoader
{
    private readonly IDonationReadRepository _donationReadRepository;
    private readonly IAccountMetadataCache _accountMetadataCache;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;

    public DonationDataCacheLoader(
        IDonationReadRepository donationReadRepository,
        IAccountMetadataCache accountMetadataCache,
        ICacheInvalidationVersion cacheInvalidationVersion)
    {
        _donationReadRepository = donationReadRepository;
        _accountMetadataCache = accountMetadataCache;
        _cacheInvalidationVersion = cacheInvalidationVersion;
    }

    public async Task<DonationDataCacheEntry> LoadAsync(
        UserAccountContextAccount account,
        DateRange queryRange,
        DonationDataCacheKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isIntern = !string.IsNullOrWhiteSpace(account.InternName);
        var subAccounts = isIntern
            ? []
            : _accountMetadataCache.TryGet(
                account.AccountId,
                _cacheInvalidationVersion.GetGeneration(CacheDataDomains.AccountMetadata),
                out var metadata)
                ? metadata.SubAccounts
                : await _donationReadRepository.GetSubAccountsByAccountIdAsync(account.AccountId);
        cancellationToken.ThrowIfCancellationRequested();

        var donations = isIntern
            ? await _donationReadRepository.GetInternDonationsByInternNameAndDateRangeAsync(
                account.InternName,
                queryRange.StartDate,
                queryRange.EndDate)
            : await _donationReadRepository.GetDonationsForAccountScopeAsync(
                account.AccountId,
                account.Fund,
                queryRange.StartDate,
                queryRange.EndDate);

        var records = (donations ?? [])
            .Select(d => new DonationCacheRecord(
                d.Id,
                d.DonorId,
                d.DonorDisplayName ?? string.Empty,
                d.Date,
                d.Amount,
                d.Frequency,
                d.Fund,
                d.AccountName,
                d.SoftCreditName,
                d.Addressee,
                d.IsAnonymous))
            .ToList();

        var subAccountRecords = (subAccounts ?? [])
            .Select(s => new SubAccountCacheRecord(s.Id, s.AccountId, s.SubFund, s.Kind))
            .ToList();

        return new DonationDataCacheEntry(key, records, subAccountRecords, DateTime.UtcNow);
    }
}

public sealed class AccountingDataCacheLoader : IAccountingDataCacheLoader
{
    private readonly IExpenseReadRepository _expenseReadRepository;

    public AccountingDataCacheLoader(IExpenseReadRepository expenseReadRepository)
    {
        _expenseReadRepository = expenseReadRepository;
    }

    public async Task<AccountingDataCacheEntry> LoadAsync(
        UserAccountContextAccount account,
        DateRange queryRange,
        AccountingDataCacheKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var accounting = await _expenseReadRepository.GetAccountingDataForAccountAsync(
            account.AccountingClass,
            account.AccountNumber,
            queryRange.StartDate,
            queryRange.EndDate);

        var records = (accounting ?? [])
            .Select(a => new AccountingCacheRecord(
                a.Id,
                a.Date,
                a.Num,
                a.Amount,
                a.Account,
                a.Type,
                a.AccountingClass,
                a.AccountNumber))
            .ToList();

        return new AccountingDataCacheEntry(key, records, DateTime.UtcNow);
    }
}
