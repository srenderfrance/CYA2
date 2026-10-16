using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.Utilities;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Cya2.Application.Services;

public sealed class AdminPreloadService : IAdminPreloadService
{
    private readonly AdminFundReadService _fundReadService;
    private readonly UserManagementService _userManagementService;
    private readonly IAccountCalculationService _accountCalculationService;
    private readonly IAdminSharedCache _sharedCache;
    private readonly IAccountMetadataCache _accountMetadataCache;
    private readonly IAccountBalanceCache _accountBalanceCache;
    private readonly IFundReferenceService _fundReferenceService;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
    private readonly IAdminCacheWarmupCoordinator _cacheWarmupCoordinator;
    private readonly IDonationReadRepository _donationReadRepository;
    private readonly IExpenseReadRepository _expenseReadRepository;
    private readonly ILogger<AdminPreloadService> _logger;
    private readonly object _sync = new();
    private long _localGeneration = -1;
    private Lazy<Task<IReadOnlyList<Account>>>? _accounts;
    private Lazy<Task<IReadOnlyList<SubAccount>>>? _subAccounts;
    private Lazy<Task<IReadOnlyList<AdminUserDto>>>? _staff;
    private Lazy<Task<IReadOnlyList<AdminAccountOverviewDto>>>? _overview;

    public AdminPreloadService(
        AdminFundReadService fundReadService,
        UserManagementService userManagementService,
        IAccountCalculationService accountCalculationService,
        IAdminSharedCache sharedCache,
        IAccountMetadataCache accountMetadataCache,
        IAccountBalanceCache accountBalanceCache,
        IFundReferenceService fundReferenceService,
        ICacheInvalidationVersion cacheInvalidationVersion,
        IAdminCacheWarmupCoordinator cacheWarmupCoordinator,
        IDonationReadRepository donationReadRepository,
        IExpenseReadRepository expenseReadRepository,
        ILogger<AdminPreloadService> logger)
    {
        _fundReadService = fundReadService;
        _userManagementService = userManagementService;
        _accountCalculationService = accountCalculationService;
        _sharedCache = sharedCache;
        _accountMetadataCache = accountMetadataCache;
        _accountBalanceCache = accountBalanceCache;
        _fundReferenceService = fundReferenceService;
        _cacheInvalidationVersion = cacheInvalidationVersion;
        _cacheWarmupCoordinator = cacheWarmupCoordinator;
        _donationReadRepository = donationReadRepository;
        _expenseReadRepository = expenseReadRepository;
        _logger = logger;
    }

    public async Task<AdminPreloadState> PreloadMetadataAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await GetAccountsAsync(cancellationToken);
        var subAccounts = await GetSubAccountsAsync(cancellationToken);
        var staff = await GetStaffAsync(cancellationToken);
        return new AdminPreloadState
        {
            Accounts = accounts,
            SubAccounts = subAccounts,
            Staff = staff
        };
    }

    public Task<IReadOnlyList<AdminAccountOverviewDto>> RebuildOverviewFromDataAsync(
        CacheRebuildData data,
        CancellationToken cancellationToken = default)
    {
        var (startDate, endDate) = GetOverviewRange();
        var generation = _cacheInvalidationVersion.Current;
        var result = new List<AdminAccountOverviewDto>(data.Accounts.Count);

        foreach (var account in data.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contextAccount = ToContextAccount(account);
            var donations = data.DonationsByAccountId.TryGetValue(account.AccountId, out var accountDonations)
                ? accountDonations
                : [];
            var accounting = data.AccountingByAccountId.TryGetValue(account.AccountId, out var accountAccounting)
                ? accountAccounting
                : [];
            var totals = _accountCalculationService.CalculateDonationTotalsFromData(
                contextAccount,
                donations,
                data.SubAccounts.Where(subAccount => subAccount.AccountId == account.AccountId),
                startDate,
                endDate);
            var balance = _accountCalculationService.CalculateBalanceFromData(
                contextAccount,
                accounting,
                null,
                endDate);

            _accountBalanceCache.Set(
                account.AccountId,
                account.AccountingClass,
                account.AccountNumber,
                account.BalanceAdjustment,
                generation,
                endDate,
                string.Empty,
                balance.TotalBalance);

            result.Add(new AdminAccountOverviewDto
            {
                Fund = account.Fund ?? string.Empty,
                CurrentBalance = balance.TotalBalance,
                Last12MonthsDonations = totals.TotalDonations
            });
        }

        _sharedCache.SetOverview(generation, startDate, endDate, result);
        return Task.FromResult<IReadOnlyList<AdminAccountOverviewDto>>(result);
    }

    public Task<AdminPreloadState> PreloadAsync(string userId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized(userId);
        return LoadStateAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        await _fundReferenceService.GetAllAsync(cancellationToken);
        ResetForGenerationChange();
        if (_sharedCache.TryGetAccounts(_cacheInvalidationVersion.Current, out var sharedAccounts))
        {
            return sharedAccounts;
        }

        EnsureInitialized();
        return await _accounts!.Value.WaitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken cancellationToken = default)
    {
        ResetForGenerationChange();
        if (_sharedCache.TryGetSubAccounts(_cacheInvalidationVersion.Current, out var sharedSubAccounts))
        {
            return sharedSubAccounts;
        }

        EnsureInitialized();
        return await _subAccounts!.Value.WaitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminUserDto>> GetStaffAsync(CancellationToken cancellationToken = default)
    {
        ResetForGenerationChange();
        if (_sharedCache.TryGetStaff(_cacheInvalidationVersion.Current, out var sharedStaff))
        {
            return sharedStaff;
        }

        EnsureInitialized();
        return await _staff!.Value.WaitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminAccountOverviewDto>> GetAccountOverviewAsync(CancellationToken cancellationToken = default)
    {
        ResetForGenerationChange();
        var (startDate, endDate) = GetOverviewRange();
        if (_sharedCache.TryGetOverview(_cacheInvalidationVersion.Current, startDate, endDate, out var sharedOverview))
        {
            return sharedOverview;
        }

        await _cacheWarmupCoordinator.WaitForWarmupAsync(cancellationToken);
        if (_sharedCache.TryGetOverview(_cacheInvalidationVersion.Current, startDate, endDate, out sharedOverview))
        {
            return sharedOverview;
        }

        EnsureInitialized();
        return await _overview!.Value.WaitAsync(cancellationToken);
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _accounts = null;
            _subAccounts = null;
            _staff = null;
            _overview = null;
            _localGeneration = _cacheInvalidationVersion.Current;
        }
    }

    private async Task<AdminPreloadState> LoadStateAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var accounts = await GetAccountsAsync(cancellationToken);
        _logger.LogInformation("Admin preload accounts loaded: count={Count} elapsedMs={ElapsedMs}", accounts.Count, stopwatch.ElapsedMilliseconds);
        var subAccounts = await GetSubAccountsAsync(cancellationToken);
        _logger.LogInformation("Admin preload subaccounts loaded: count={Count} elapsedMs={ElapsedMs}", subAccounts.Count, stopwatch.ElapsedMilliseconds);
        var staff = await GetStaffAsync(cancellationToken);
        _logger.LogInformation("Admin preload staff loaded: count={Count} elapsedMs={ElapsedMs}", staff.Count, stopwatch.ElapsedMilliseconds);
        var overview = await GetAccountOverviewAsync(cancellationToken);
        _logger.LogInformation("Admin preload overview loaded: count={Count} elapsedMs={ElapsedMs}", overview.Count, stopwatch.ElapsedMilliseconds);

        var state = new AdminPreloadState
        {
            Accounts = accounts,
            SubAccounts = subAccounts,
            Staff = staff,
            AccountOverview = overview
        };
        _logger.LogInformation("Admin preload completed: elapsedMs={ElapsedMs}", stopwatch.ElapsedMilliseconds);
        return state;
    }

    private void EnsureInitialized(string? userId = null)
    {
        lock (_sync)
        {
            _accounts ??= new Lazy<Task<IReadOnlyList<Account>>>(LoadAccountsAsync);
            _subAccounts ??= new Lazy<Task<IReadOnlyList<SubAccount>>>(LoadSubAccountsAsync);
            _staff ??= new Lazy<Task<IReadOnlyList<AdminUserDto>>>(LoadStaffAsync);
            _overview ??= new Lazy<Task<IReadOnlyList<AdminAccountOverviewDto>>>(LoadOverviewAsync);
        }
    }

    private async Task<IReadOnlyList<Account>> LoadAccountsAsync()
    {
        var accounts = await _fundReadService.GetAllAccountsAsync() ?? new List<Account>();
        _sharedCache.SetAccounts(_cacheInvalidationVersion.Current, accounts);
        return accounts;
    }

    private async Task<IReadOnlyList<SubAccount>> LoadSubAccountsAsync()
    {
        var subAccounts = await _fundReadService.GetSubAccountsAsync();
        var generation = _cacheInvalidationVersion.AccountMetadata;
        _sharedCache.SetSubAccounts(_cacheInvalidationVersion.Current, subAccounts);

        var accounts = await GetAccountsAsync();
        foreach (var account in accounts)
        {
            _accountMetadataCache.Set(
                account.AccountId,
                generation,
                new Cya2.Application.Models.AccountMetadataSnapshot(
                    account,
                    subAccounts.Where(subAccount => subAccount.AccountId == account.AccountId)));
        }

        return subAccounts;
    }

    private async Task<IReadOnlyList<AdminUserDto>> LoadStaffAsync()
    {
        var staff = await _userManagementService.GetAdminUsersAsync() ?? new List<AdminUserDto>();
        _sharedCache.SetStaff(_cacheInvalidationVersion.Current, staff);
        return staff;
    }

    private async Task<IReadOnlyList<AdminAccountOverviewDto>> LoadOverviewAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var accounts = await GetAccountsAsync();
        var subAccounts = await GetSubAccountsAsync();
        var (trailingStart, trailingEnd) = GetOverviewRange();
        _logger.LogInformation(
            "Admin overview bulk fallback started: accountCount={Count} start={Start:yyyy-MM-dd} end={End:yyyy-MM-dd}",
            accounts.Count,
            trailingStart,
            trailingEnd);
        var normalAccounts = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.Fund))
            .Select(account => (
                account.AccountId,
                account.Fund,
                MergedSubFunds: (IReadOnlyList<string>)subAccounts
                    .Where(subAccount => subAccount.AccountId == account.AccountId &&
                        string.Equals(subAccount.Kind, "Merged", StringComparison.OrdinalIgnoreCase))
                    .Select(subAccount => subAccount.SubFund)
                    .ToList()))
            .ToList();
        var donationTask = _donationReadRepository.GetDonationsForAccountsAsync(normalAccounts, trailingStart, trailingEnd);
        var accountingTask = _expenseReadRepository.GetAccountingDataForAccountsAsync(
            accounts.Select(account => (account.AccountId, account.AccountingClass, account.AccountNumber)).ToList(),
            trailingStart,
            trailingEnd);
        await Task.WhenAll(donationTask, accountingTask);

        var data = new CacheRebuildData
        {
            Accounts = accounts,
            SubAccounts = subAccounts,
            DonationsByAccountId = donationTask.Result,
            AccountingByAccountId = accountingTask.Result
        };
        var result = await RebuildOverviewFromDataAsync(data);
        _logger.LogInformation(
            "Admin overview bulk fallback completed: accountCount={Count} elapsedMs={ElapsedMs}",
            result.Count,
            stopwatch.ElapsedMilliseconds);
        return result;
    }

    private void ResetForGenerationChange()
    {
        var currentGeneration = _cacheInvalidationVersion.Current;
        lock (_sync)
        {
            if (_localGeneration == currentGeneration)
            {
                return;
            }

            _accounts = null;
            _subAccounts = null;
            _staff = null;
            _overview = null;
            _localGeneration = currentGeneration;
        }
    }

    private static (DateTime StartDate, DateTime EndDate) GetOverviewRange()
    {
        var endDate = DateTime.UtcNow.Date;
        return (endDate.AddMonths(-12), endDate);
    }

    private static UserAccountContextAccount ToContextAccount(Account account)
    {
        return new UserAccountContextAccount
        {
            AccountId = account.AccountId,
            Fund = account.Fund ?? string.Empty,
            AccountingClass = account.AccountingClass ?? string.Empty,
            AccountNumber = account.AccountNumber ?? string.Empty,
            CreatedAt = account.CreatedAt,
            Overhead = account.Overhead,
            SoftCredit = account.SoftCredit ?? string.Empty,
            BalanceAdjustment = account.BalanceAdjustment,
            OtherFunds = account.OtherFunds
        };
    }
}
