using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Utilities;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Services;

public sealed class AccountFactDataService : IAccountFactDataService
{
    private readonly IDonationDataCache _donations;
    private readonly IDonationDataCacheLoader _donationLoader;
    private readonly IAccountingDataCache _accounting;
    private readonly IAccountingDataCacheLoader _accountingLoader;
    private readonly IUserDonationDataCache _userDonations;
    private readonly IUserAccountingDataCache _userAccounting;
    private readonly IDonationReadRepository _donationRepository;
    private readonly IExpenseReadRepository _expenseRepository;
    private readonly IAccountMetadataCache _accountMetadataCache;
    private readonly ICacheInvalidationVersion _versions;

    public AccountFactDataService(IDonationDataCache donations, IDonationDataCacheLoader donationLoader, IAccountingDataCache accounting, IAccountingDataCacheLoader accountingLoader, IUserDonationDataCache userDonations, IUserAccountingDataCache userAccounting, IDonationReadRepository donationRepository, IExpenseReadRepository expenseRepository, IAccountMetadataCache accountMetadataCache, ICacheInvalidationVersion versions)
    {
        _donations = donations;
        _donationLoader = donationLoader;
        _accounting = accounting;
        _accountingLoader = accountingLoader;
        _userDonations = userDonations;
        _userAccounting = userAccounting;
        _donationRepository = donationRepository;
        _expenseRepository = expenseRepository;
        _accountMetadataCache = accountMetadataCache;
        _versions = versions;
    }

    public async Task<DonationCalculationData> GetDonationDataAsync(UserAccountContextAccount account, string userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(account.InternName))
        {
            var internRows = await _donationRepository.GetInternDonationsByInternNameAndDateRangeAsync(account.InternName ?? string.Empty, startDate, endDate);
            return new DonationCalculationData { Donations = internRows ?? [], SubAccounts = [] };
        }

        var standard = AccountFactCacheWindow.GetStandardWindow(DateTime.UtcNow);
        var standardKey = new DonationDataCacheKey(account.AccountId, _versions.Donations, standard.StartDate, standard.EndDate).Normalize();
        var shared = await _donations.GetOrCreateAsync(standardKey, token => _donationLoader.LoadAsync(account, standard, standardKey, token), cancellationToken);
        var records = shared.Donations.Select(ToDonationRecord).ToList();
        var subAccounts = shared.SubAccounts.Select(ToSubAccount).ToList();

        var donationOverflowKey = new UserDonationDataCacheKey(userId, account.AccountId, _versions.Donations);
        _userDonations.TryGet(donationOverflowKey, out var overflowEntry);
        foreach (var missing in GetMissingRanges(startDate, endDate, standard, overflowEntry?.Coverage))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var overflow = await GetUserDonationOverflowAsync(account, userId, missing, cancellationToken);
            records.AddRange(overflow.Donations.Select(ToDonationRecord));
            if (subAccounts.Count == 0) subAccounts.AddRange(overflow.SubAccounts.Select(ToSubAccount));
        }

        if (overflowEntry is not null)
        {
            records.AddRange(overflowEntry.Donations.Select(ToDonationRecord));
            if (subAccounts.Count == 0) subAccounts.AddRange(overflowEntry.SubAccounts.Select(ToSubAccount));
        }

        return new DonationCalculationData { Donations = records.Where(r => r.Date.Date >= startDate.Date && r.Date.Date <= endDate.Date).GroupBy(r => r.Id).Select(g => g.First()).ToList(), SubAccounts = subAccounts };
    }

    public async Task<List<AccountingRecord>> GetAccountingDataAsync(UserAccountContextAccount account, string userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(account.InternName)) return [];
        var standard = AccountFactCacheWindow.GetStandardWindow(DateTime.UtcNow);
        var standardKey = new AccountingDataCacheKey(account.AccountId, account.AccountingClass, account.AccountNumber, _versions.Accounting, standard.StartDate, standard.EndDate).Normalize();
        var shared = await _accounting.GetOrCreateAsync(standardKey, token => _accountingLoader.LoadAsync(account, standard, standardKey, token), cancellationToken);
        var records = shared.Transactions.Select(ToAccountingRecord).ToList();
        var accountingOverflowKey = new UserAccountingDataCacheKey(userId, account.AccountId, account.AccountingClass, account.AccountNumber, _versions.Accounting);
        _userAccounting.TryGet(accountingOverflowKey, out var overflowEntry);
        foreach (var missing in GetMissingRanges(startDate, endDate, standard, overflowEntry?.Coverage))
        {
            var overflow = await GetUserAccountingOverflowAsync(account, userId, missing, cancellationToken);
            records.AddRange(overflow.Transactions.Select(ToAccountingRecord));
        }
        if (overflowEntry is not null) records.AddRange(overflowEntry.Transactions.Select(ToAccountingRecord));
        return records.Where(r => r.Date.Date >= startDate.Date && r.Date.Date <= endDate.Date).GroupBy(r => r.Id).Select(g => g.First()).ToList();
    }

    private async Task<UserDonationDataCacheEntry> GetUserDonationOverflowAsync(UserAccountContextAccount account, string userId, DateRange range, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return new UserDonationDataCacheEntry(new UserDonationDataCacheKey(string.Empty, account.AccountId, _versions.Donations), [new UserFactCoverage(range.StartDate, range.EndDate)], [], [], DateTime.UtcNow);
        var key = new UserDonationDataCacheKey(userId, account.AccountId, _versions.Donations).Normalize();
        return await _userDonations.AddCoverageAsync(key, range.StartDate, range.EndDate, async token =>
        {
            var metadataGeneration = _versions.GetGeneration(CacheDataDomains.AccountMetadata);
            var subs = _accountMetadataCache.TryGet(account.AccountId, metadataGeneration, out var metadata)
                ? metadata.SubAccounts
                : await _donationRepository.GetSubAccountsByAccountIdAsync(account.AccountId);
            var rows = await _donationRepository.GetDonationsForAccountScopeAsync(account.AccountId, account.Fund, range.StartDate, range.EndDate);
            return new UserDonationDataCacheEntry(key, [new UserFactCoverage(range.StartDate, range.EndDate)], (rows ?? []).Select(ToDonationCacheRecord), (subs ?? []).Select(s => new SubAccountCacheRecord(s.Id, s.AccountId, s.SubFund, s.Kind)), DateTime.UtcNow);
        }, cancellationToken);
    }

    private async Task<UserAccountingDataCacheEntry> GetUserAccountingOverflowAsync(UserAccountContextAccount account, string userId, DateRange range, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return new UserAccountingDataCacheEntry(new UserAccountingDataCacheKey(string.Empty, account.AccountId, account.AccountingClass, account.AccountNumber, _versions.Accounting), [new UserFactCoverage(range.StartDate, range.EndDate)], [], DateTime.UtcNow);
        var key = new UserAccountingDataCacheKey(userId, account.AccountId, account.AccountingClass, account.AccountNumber, _versions.Accounting).Normalize();
        return await _userAccounting.AddCoverageAsync(key, range.StartDate, range.EndDate, async token =>
        {
            var rows = await _expenseRepository.GetAccountingDataForAccountAsync(account.AccountingClass, account.AccountNumber, range.StartDate, range.EndDate);
            return new UserAccountingDataCacheEntry(key, [new UserFactCoverage(range.StartDate, range.EndDate)], (rows ?? []).Select(ToAccountingCacheRecord), DateTime.UtcNow);
        }, cancellationToken);
    }

    private static IEnumerable<DateRange> GetMissingRanges(DateTime startDate, DateTime endDate, DateRange standard, IReadOnlyList<UserFactCoverage>? overflowCoverage)
    {
        var ranges = new List<UserFactCoverage> { new(standard.StartDate, standard.EndDate) };
        if (overflowCoverage is not null) ranges.AddRange(overflowCoverage);
        var cursor = startDate.Date;
        foreach (var range in ranges.OrderBy(r => r.StartDate))
        {
            if (range.EndDate < cursor || range.StartDate > endDate.Date) continue;
            if (range.StartDate > cursor) yield return new DateRange(cursor, range.StartDate.AddDays(-1));
            if (range.EndDate >= cursor) cursor = range.EndDate.AddDays(1);
            if (cursor > endDate.Date) yield break;
        }
        if (cursor <= endDate.Date) yield return new DateRange(cursor, endDate.Date);
    }

    private static DonationCacheRecord ToDonationCacheRecord(DonationRecord d) => new(d.Id, d.DonorId, d.DonorDisplayName ?? string.Empty, d.Date, d.Amount, d.Frequency, d.Fund, d.AccountName, d.SoftCreditName, d.Addressee, d.IsAnonymous);
    private static DonationRecord ToDonationRecord(DonationCacheRecord d) => new() { Id = d.Id, DonorId = d.DonorId, DonorDisplayName = d.DonorDisplayName, Date = d.Date, Amount = d.Amount, Frequency = d.Frequency, Fund = d.Fund, AccountName = d.AccountName, SoftCreditName = d.SoftCreditName, Addressee = d.Addressee, IsAnonymous = d.IsAnonymous };
    private static SubAccount ToSubAccount(SubAccountCacheRecord s) => new() { Id = s.Id, AccountId = s.AccountId, SubFund = s.SubFund, Kind = s.Kind };
    private static AccountingCacheRecord ToAccountingCacheRecord(AccountingRecord a) => new(a.Id, a.Date, a.Num, a.Amount, a.Account, a.Type, a.AccountingClass, a.AccountNumber);
    private static AccountingRecord ToAccountingRecord(AccountingCacheRecord a) => new() { Id = a.Id, Date = a.Date, Num = a.Number, Amount = a.Amount, Account = a.Account, Type = a.Type, AccountingClass = a.AccountingClass, AccountNumber = a.AccountNumber, DateCreated = DateTime.MinValue };
}
