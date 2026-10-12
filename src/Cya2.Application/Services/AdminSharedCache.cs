using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Entities;

namespace Cya2.Application.Services;

public sealed class AdminSharedCache : IAdminSharedCache
{
    private const int MaxOverviewEntries = 4;
    private readonly object _sync = new();
    private readonly Dictionary<OverviewKey, IReadOnlyList<AdminAccountOverviewDto>> _overviews = new();
    private long _generation = -1;
    private IReadOnlyList<Account>? _accounts;
    private IReadOnlyList<SubAccount>? _subAccounts;
    private IReadOnlyList<AdminUserDto>? _staff;

    public bool TryGetAccounts(long generation, out IReadOnlyList<Account> accounts)
    {
        lock (_sync)
        {
            if (_generation == generation && _accounts is not null)
            {
                accounts = _accounts;
                return true;
            }
        }

        accounts = Array.Empty<Account>();
        return false;
    }

    public void SetAccounts(long generation, IReadOnlyList<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        lock (_sync)
        {
            PrepareGeneration(generation);
            _accounts = accounts.ToArray();
        }
    }

    public bool TryGetSubAccounts(long generation, out IReadOnlyList<SubAccount> subAccounts)
    {
        lock (_sync)
        {
            if (_generation == generation && _subAccounts is not null)
            {
                subAccounts = _subAccounts;
                return true;
            }
        }

        subAccounts = Array.Empty<SubAccount>();
        return false;
    }

    public void SetSubAccounts(long generation, IReadOnlyList<SubAccount> subAccounts)
    {
        ArgumentNullException.ThrowIfNull(subAccounts);
        lock (_sync)
        {
            PrepareGeneration(generation);
            _subAccounts = subAccounts.ToArray();
        }
    }

    public bool TryGetStaff(long generation, out IReadOnlyList<AdminUserDto> staff)
    {
        lock (_sync)
        {
            if (_generation == generation && _staff is not null)
            {
                staff = _staff;
                return true;
            }
        }

        staff = Array.Empty<AdminUserDto>();
        return false;
    }

    public void SetStaff(long generation, IReadOnlyList<AdminUserDto> staff)
    {
        ArgumentNullException.ThrowIfNull(staff);
        lock (_sync)
        {
            PrepareGeneration(generation);
            _staff = staff.ToArray();
        }
    }

    public bool TryGetOverview(long generation, DateTime startDate, DateTime endDate, out IReadOnlyList<AdminAccountOverviewDto> overview)
    {
        var key = new OverviewKey(generation, startDate.Date, endDate.Date);
        lock (_sync)
        {
            if (_overviews.TryGetValue(key, out var cached))
            {
                overview = cached;
                return true;
            }
        }

        overview = Array.Empty<AdminAccountOverviewDto>();
        return false;
    }

    public void SetOverview(long generation, DateTime startDate, DateTime endDate, IReadOnlyList<AdminAccountOverviewDto> overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        var key = new OverviewKey(generation, startDate.Date, endDate.Date);
        lock (_sync)
        {
            PrepareGeneration(generation);
            _overviews[key] = overview.ToArray();
            while (_overviews.Count > MaxOverviewEntries)
            {
                var oldest = _overviews.Keys.OrderBy(item => item.StartDate).ThenBy(item => item.EndDate).First();
                _overviews.Remove(oldest);
            }
        }
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _generation = -1;
            _accounts = null;
            _subAccounts = null;
            _staff = null;
            _overviews.Clear();
        }
    }

    private void PrepareGeneration(long generation)
    {
        if (_generation == generation)
        {
            return;
        }

        _generation = generation;
        _accounts = null;
        _subAccounts = null;
        _staff = null;
        _overviews.Clear();
    }

    private readonly record struct OverviewKey(long Generation, DateTime StartDate, DateTime EndDate);
}
