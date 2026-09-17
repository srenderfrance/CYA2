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
    private readonly IDonationReadRepository _donationReadRepository;
    private readonly IAccountCalculationService _accountCalculationService;
    private readonly ILogger<AdminPreloadService> _logger;
    private readonly object _sync = new();
    private Lazy<Task<IReadOnlyList<Account>>>? _accounts;
    private Lazy<Task<IReadOnlyList<SubAccount>>>? _subAccounts;
    private Lazy<Task<IReadOnlyList<AdminUserDto>>>? _staff;
    private Lazy<Task<IReadOnlyList<AdminAccountOverviewDto>>>? _overview;

    public AdminPreloadService(
        AdminFundReadService fundReadService,
        UserManagementService userManagementService,
        IDonationReadRepository donationReadRepository,
        IAccountCalculationService accountCalculationService,
        ILogger<AdminPreloadService> logger)
    {
        _fundReadService = fundReadService;
        _userManagementService = userManagementService;
        _donationReadRepository = donationReadRepository;
        _accountCalculationService = accountCalculationService;
        _logger = logger;
    }

    public Task<AdminPreloadState> PreloadAsync(string userId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized(userId);
        return LoadStateAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return await _accounts!.Value.WaitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return await _subAccounts!.Value.WaitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminUserDto>> GetStaffAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return await _staff!.Value.WaitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminAccountOverviewDto>> GetAccountOverviewAsync(CancellationToken cancellationToken = default)
    {
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
        => await _fundReadService.GetAllAccountsAsync() ?? new List<Account>();

    private async Task<IReadOnlyList<SubAccount>> LoadSubAccountsAsync()
        => await _fundReadService.GetSubAccountsAsync();

    private async Task<IReadOnlyList<AdminUserDto>> LoadStaffAsync()
        => await _userManagementService.GetAdminUsersAsync() ?? new List<AdminUserDto>();

    private async Task<IReadOnlyList<AdminAccountOverviewDto>> LoadOverviewAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var accounts = await GetAccountsAsync();
        _logger.LogInformation("Admin overview calculation started: accountCount={Count}", accounts.Count);
        var trailingStart = DateTime.UtcNow.Date.AddMonths(-12);
        var trailingEnd = DateTime.UtcNow.Date;
        var nonInternAccounts = accounts
            .Where(account => !InternAccountUtility.IsInternFund(account.Fund))
            .ToList();
        var subAccountsTask = _fundReadService.GetSubAccountsAsync();
        var balanceAccounts = accounts.Select(ToContextAccount).ToList();
        var balancesTask = _accountCalculationService.CalculateBalancesAsync(balanceAccounts, null, trailingEnd);
        var subAccounts = await subAccountsTask;
        var donationFunds = nonInternAccounts
            .SelectMany(account => new[] { account.Fund }
                .Concat(subAccounts
                    .Where(sub => sub.AccountId == account.AccountId && string.Equals(sub.Kind?.Trim(), "Merged", StringComparison.OrdinalIgnoreCase))
                    .Select(sub => sub.SubFund)))
            .Where(fund => !string.IsNullOrWhiteSpace(fund))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var donationsTask = _donationReadRepository.GetDonationsByFundNamesAndDateRangeAsync(donationFunds, trailingStart, trailingEnd);
        var balances = await balancesTask;
        var donations = await donationsTask;
        var subAccountsByAccount = subAccounts
            .GroupBy(sub => sub.AccountId)
            .ToDictionary(group => group.Key, group => (IEnumerable<SubAccount>)group.ToList());
        using var concurrencyGate = new SemaphoreSlim(2);
        var tasks = accounts.Select(async account =>
        {
            await concurrencyGate.WaitAsync();
            var accountStopwatch = Stopwatch.StartNew();
            try
            {
                var contextAccount = ToContextAccount(account);
                var donationTotals = InternAccountUtility.IsInternFund(account.Fund)
                    ? await _accountCalculationService.CalculateDonationTotalsAsync(contextAccount, trailingStart, trailingEnd)
                    : _accountCalculationService.CalculateDonationTotalsFromData(
                        contextAccount,
                        donations,
                        subAccountsByAccount.GetValueOrDefault(account.AccountId, []),
                        trailingStart,
                        trailingEnd);

                return new AdminAccountOverviewDto
                {
                    Fund = account.Fund ?? string.Empty,
                    CurrentBalance = balances.GetValueOrDefault(account.AccountId)?.TotalBalance ?? 0m,
                    Last12MonthsDonations = donationTotals.TotalDonations
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Admin overview calculation failed for accountId={AccountId} fund='{Fund}' elapsedMs={ElapsedMs}", account.AccountId, account.Fund, accountStopwatch.ElapsedMilliseconds);
                return new AdminAccountOverviewDto { Fund = account.Fund ?? string.Empty };
            }
            finally
            {
                _logger.LogDebug("Admin overview account completed: accountId={AccountId} fund='{Fund}' elapsedMs={ElapsedMs}", account.AccountId, account.Fund, accountStopwatch.ElapsedMilliseconds);
                concurrencyGate.Release();
            }
        });

        var result = await Task.WhenAll(tasks);
        _logger.LogInformation("Admin overview calculation completed: accountCount={Count} elapsedMs={ElapsedMs}", result.Length, stopwatch.ElapsedMilliseconds);
        return result;
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
