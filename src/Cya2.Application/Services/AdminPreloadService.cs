using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
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
    private readonly IAccountBalanceCache _accountBalanceCache;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
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
        IAccountBalanceCache accountBalanceCache,
        ICacheInvalidationVersion cacheInvalidationVersion,
        ILogger<AdminPreloadService> logger)
    {
        _fundReadService = fundReadService;
        _userManagementService = userManagementService;
        _accountCalculationService = accountCalculationService;
        _sharedCache = sharedCache;
        _accountBalanceCache = accountBalanceCache;
        _cacheInvalidationVersion = cacheInvalidationVersion;
        _logger = logger;
    }

    public Task<AdminPreloadState> PreloadAsync(string userId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized(userId);
        return LoadStateAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
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
        _sharedCache.SetSubAccounts(_cacheInvalidationVersion.Current, subAccounts);
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
        _logger.LogInformation("Admin overview calculation started: accountCount={Count}", accounts.Count);
        var trailingStart = DateTime.UtcNow.Date.AddMonths(-12);
        var trailingEnd = DateTime.UtcNow.Date;
        _logger.LogInformation(
            "Admin overview donation range: start={Start:yyyy-MM-dd} end={End:yyyy-MM-dd}",
            trailingStart,
            trailingEnd);
        var balanceAccounts = accounts.Select(ToContextAccount).ToList();
        var balancesTask = _accountCalculationService.CalculateBalancesAsync(balanceAccounts, null, trailingEnd);
        var balances = await balancesTask;
        var generation = _cacheInvalidationVersion.Current;
        foreach (var account in balanceAccounts)
        {
            if (balances.TryGetValue(account.AccountId, out var balance))
            {
                _accountBalanceCache.Set(
                    account.AccountId,
                    account.AccountingClass,
                    account.AccountNumber,
                    account.BalanceAdjustment,
                    generation,
                    trailingEnd,
                    balance.TotalBalance);
            }
        }
        using var concurrencyGate = new SemaphoreSlim(2);
        var tasks = accounts.Select(async account =>
        {
            await concurrencyGate.WaitAsync();
            var accountStopwatch = Stopwatch.StartNew();
            try
            {
                var contextAccount = ToContextAccount(account);
                var donationTotals = await _accountCalculationService.CalculateDonationTotalsAsync(
                    contextAccount,
                    trailingStart,
                    trailingEnd);

                _logger.LogInformation(
                    "Admin overview donation total: accountId={AccountId} total={Total} elapsedMs={ElapsedMs}",
                    account.AccountId,
                    donationTotals.TotalDonations,
                    accountStopwatch.ElapsedMilliseconds);

                return new AdminAccountOverviewDto
                {
                    Fund = account.Fund ?? string.Empty,
                    CurrentBalance = balances.GetValueOrDefault(account.AccountId)?.TotalBalance ?? 0m,
                    Last12MonthsDonations = donationTotals.TotalDonations
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Admin overview calculation failed for accountId={AccountId} elapsedMs={ElapsedMs}", account.AccountId, accountStopwatch.ElapsedMilliseconds);
                return new AdminAccountOverviewDto { Fund = account.Fund ?? string.Empty };
            }
            finally
            {
                _logger.LogDebug("Admin overview account completed: accountId={AccountId} elapsedMs={ElapsedMs}", account.AccountId, accountStopwatch.ElapsedMilliseconds);
                concurrencyGate.Release();
            }
        });

        var result = await Task.WhenAll(tasks);
        _sharedCache.SetOverview(_cacheInvalidationVersion.Current, trailingStart, trailingEnd, result);
        _logger.LogInformation("Admin overview calculation completed: accountCount={Count} elapsedMs={ElapsedMs}", result.Length, stopwatch.ElapsedMilliseconds);
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
