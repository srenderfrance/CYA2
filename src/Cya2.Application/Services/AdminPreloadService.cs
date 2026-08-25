using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.Utilities;

namespace Cya2.Application.Services;

public sealed class AdminPreloadService : IAdminPreloadService
{
    private readonly AdminFundReadService _fundReadService;
    private readonly UserManagementService _userManagementService;
    private readonly IFinancialDashboardReadRepository _financialDashboardReadRepository;
    private readonly object _sync = new();
    private Lazy<Task<IReadOnlyList<Account>>>? _accounts;
    private Lazy<Task<IReadOnlyList<SubAccount>>>? _subAccounts;
    private Lazy<Task<IReadOnlyList<AdminUserDto>>>? _staff;
    private Lazy<Task<IReadOnlyList<AdminAccountOverviewDto>>>? _overview;

    public AdminPreloadService(
        AdminFundReadService fundReadService,
        UserManagementService userManagementService,
        IFinancialDashboardReadRepository financialDashboardReadRepository)
    {
        _fundReadService = fundReadService;
        _userManagementService = userManagementService;
        _financialDashboardReadRepository = financialDashboardReadRepository;
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
        var accounts = await GetAccountsAsync(cancellationToken);
        var subAccounts = await GetSubAccountsAsync(cancellationToken);
        var staff = await GetStaffAsync(cancellationToken);
        var overview = await GetAccountOverviewAsync(cancellationToken);

        return new AdminPreloadState
        {
            Accounts = accounts,
            SubAccounts = subAccounts,
            Staff = staff,
            AccountOverview = overview
        };
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
        var accounts = await GetAccountsAsync();
        var tasks = accounts.Select(async account =>
        {
            try
            {
                var now = DateTime.UtcNow;
                var trailingStart = now.Date.AddMonths(-12);
                var trailingEnd = now.Date;
                var balanceTask = _financialDashboardReadRepository.GetBalanceAsOfAsync(account, trailingEnd);
                var donationsTask = GetDonationTotalAsync(account, trailingStart, trailingEnd);
                await Task.WhenAll(balanceTask, donationsTask);

                return new AdminAccountOverviewDto
                {
                    Fund = account.Fund ?? string.Empty,
                    CurrentBalance = balanceTask.Result,
                    Last12MonthsDonations = donationsTask.Result
                };
            }
            catch
            {
                return new AdminAccountOverviewDto { Fund = account.Fund ?? string.Empty };
            }
        });

        return await Task.WhenAll(tasks);
    }

    private Task<decimal> GetDonationTotalAsync(Account account, DateTime startDate, DateTime endDate)
    {
        if (InternAccountUtility.IsInternFund(account.Fund) &&
            InternAccountUtility.TryGetInternDesignationName(account.Fund, out var designation))
        {
            return _financialDashboardReadRepository.GetInternDonationTotalAsync(designation, startDate, endDate);
        }

        return _financialDashboardReadRepository.GetDonationTotalAsync(account, startDate, endDate);
    }
}
