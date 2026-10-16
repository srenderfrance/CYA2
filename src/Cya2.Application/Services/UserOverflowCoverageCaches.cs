using Cya2.Application.Interfaces;
using Cya2.Application.Models;

namespace Cya2.Application.Services;

public sealed class UserDonationDataCache : IUserDonationDataCache
{
    private const int MaxKeys = 256;
    private const int MaxCoverageSegments = 3;
    private readonly object _sync = new();
    private readonly Dictionary<UserDonationDataCacheKey, (UserDonationDataCacheEntry Entry, LinkedListNode<UserDonationDataCacheKey> Node)> _entries = new();
    private readonly LinkedList<UserDonationDataCacheKey> _lru = new();
    private readonly Dictionary<(UserDonationDataCacheKey Key, DateTime Start, DateTime End), InflightLoad> _inflight = new();

    public bool TryGet(UserDonationDataCacheKey key, out UserDonationDataCacheEntry entry)
    {
        key = key.Normalize();
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var value)) { Touch(value.Node); entry = value.Entry; return true; }
        }

        entry = default!;
        return false;
    }

    public bool TryGet(UserDonationDataCacheKey key, DateTime startDate, DateTime endDate, out UserDonationDataCacheEntry entry)
    {
        key = key.Normalize();
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var value) && value.Entry.Coverage.Any(c => c.Contains(startDate, endDate)))
            {
                Touch(value.Node);
                entry = value.Entry;
                return true;
            }
        }
        entry = default!;
        return false;
    }

    public async Task<UserDonationDataCacheEntry> AddCoverageAsync(UserDonationDataCacheKey key, DateTime startDate, DateTime endDate, Func<CancellationToken, Task<UserDonationDataCacheEntry>> factory, CancellationToken cancellationToken = default)
    {
        key = key.Normalize();
        var request = (key, startDate.Date, endDate.Date);
        InflightLoad load;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing) && existing.Entry.Coverage.Any(c => c.Contains(startDate, endDate)))
            {
                Touch(existing.Node);
                return existing.Entry;
            }
            if (!_inflight.TryGetValue(request, out load!))
            {
                load = new InflightLoad();
                load.Key = request;
                _inflight[request] = load;
                load.Task = LoadAndMergeAsync(request, load, key, startDate.Date, endDate.Date, factory);
            }
        }
        return await load.Task.WaitAsync(cancellationToken);
    }

    public void RemoveUser(string userId) { lock (_sync) { foreach (var key in _entries.Keys.Where(k => string.Equals(k.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList()) RemoveCore(key); CancelInflight(load => string.Equals(load.Key.Key.UserId, userId, StringComparison.OrdinalIgnoreCase)); } }
    public void RemoveStaleGenerations(long currentGeneration) { lock (_sync) { foreach (var key in _entries.Keys.Where(k => k.DonationsGeneration != currentGeneration).ToList()) RemoveCore(key); CancelInflight(load => load.Key.Key.DonationsGeneration != currentGeneration); } }
    public void InvalidateAll() { lock (_sync) { _entries.Clear(); _lru.Clear(); CancelInflight(_ => true); } }

    private async Task<UserDonationDataCacheEntry> LoadAndMergeAsync((UserDonationDataCacheKey Key, DateTime Start, DateTime End) request, InflightLoad load, UserDonationDataCacheKey key, DateTime startDate, DateTime endDate, Func<CancellationToken, Task<UserDonationDataCacheEntry>> factory)
    {
        try
        {
            var loaded = await factory(load.CancellationTokenSource.Token);
            lock (_sync)
            {
                if (!_inflight.TryGetValue(request, out var current) || !ReferenceEquals(current, load))
                    return loaded;
                if (_entries.TryGetValue(key, out var existing))
                {
                    var merged = new UserDonationDataCacheEntry(key, MergeCoverage(existing.Entry.Coverage, new UserFactCoverage(startDate, endDate)),
                        existing.Entry.Donations.Concat(loaded.Donations).GroupBy(d => d.Id).Select(g => g.First()),
                        existing.Entry.SubAccounts.Concat(loaded.SubAccounts).GroupBy(s => s.Id).Select(g => g.First()), DateTime.UtcNow);
                    _entries[key] = (merged, existing.Node);
                    Touch(existing.Node);
                    return merged;
                }
                var node = _lru.AddFirst(key);
                _entries[key] = (new UserDonationDataCacheEntry(key, [new UserFactCoverage(startDate, endDate)], loaded.Donations, loaded.SubAccounts, DateTime.UtcNow), node);
                while (_entries.Count > MaxKeys && _lru.Last is not null) RemoveCore(_lru.Last.Value);
                return _entries[key].Entry;
            }
        }
        finally
        {
            lock (_sync)
            {
                if (_inflight.TryGetValue(request, out var current) && ReferenceEquals(current, load))
                    _inflight.Remove(request);
            }
        }
    }

    private void CancelInflight(Func<InflightLoad, bool> predicate)
    {
        foreach (var request in _inflight.Where(pair => predicate(pair.Value)).Select(pair => pair.Key).ToList())
        {
            var load = _inflight[request];
            _inflight.Remove(request);
            load.CancellationTokenSource.Cancel();
        }
    }

    private sealed class InflightLoad
    {
        public CancellationTokenSource CancellationTokenSource { get; } = new();
        public Task<UserDonationDataCacheEntry> Task { get; set; } = null!;
        public (UserDonationDataCacheKey Key, DateTime Start, DateTime End) Key { get; set; }
    }

    private static IReadOnlyList<UserFactCoverage> MergeCoverage(IEnumerable<UserFactCoverage> existing, UserFactCoverage added)
    {
        var ranges = existing.Append(added).Select(c => c.Normalize()).OrderBy(c => c.StartDate).ToList();
        var merged = new List<UserFactCoverage>();
        foreach (var range in ranges)
        {
            if (merged.Count == 0 || range.StartDate > merged[^1].EndDate.AddDays(1)) merged.Add(range);
            else merged[^1] = new UserFactCoverage(merged[^1].StartDate, range.EndDate > merged[^1].EndDate ? range.EndDate : merged[^1].EndDate);
        }
        return merged.Count <= MaxCoverageSegments ? merged : merged.OrderByDescending(c => (c.EndDate - c.StartDate).Days).Take(MaxCoverageSegments).OrderBy(c => c.StartDate).ToArray();
    }

    private void Touch(LinkedListNode<UserDonationDataCacheKey> node) { _lru.Remove(node); _lru.AddFirst(node); }
    private void RemoveCore(UserDonationDataCacheKey key) { if (_entries.Remove(key, out var value)) _lru.Remove(value.Node); }
}

public sealed class UserAccountingDataCache : IUserAccountingDataCache
{
    private const int MaxKeys = 256;
    private const int MaxCoverageSegments = 3;
    private readonly object _sync = new();
    private readonly Dictionary<UserAccountingDataCacheKey, (UserAccountingDataCacheEntry Entry, LinkedListNode<UserAccountingDataCacheKey> Node)> _entries = new();
    private readonly LinkedList<UserAccountingDataCacheKey> _lru = new();
    private readonly Dictionary<(UserAccountingDataCacheKey Key, DateTime Start, DateTime End), InflightLoad> _inflight = new();

    public bool TryGet(UserAccountingDataCacheKey key, out UserAccountingDataCacheEntry entry)
    {
        key = key.Normalize();
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var value)) { Touch(value.Node); entry = value.Entry; return true; }
        }
        entry = default!;
        return false;
    }

    public bool TryGet(UserAccountingDataCacheKey key, DateTime startDate, DateTime endDate, out UserAccountingDataCacheEntry entry)
    {
        key = key.Normalize();
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var value) && value.Entry.Coverage.Any(c => c.Contains(startDate, endDate))) { Touch(value.Node); entry = value.Entry; return true; }
        }
        entry = default!;
        return false;
    }

    public async Task<UserAccountingDataCacheEntry> AddCoverageAsync(UserAccountingDataCacheKey key, DateTime startDate, DateTime endDate, Func<CancellationToken, Task<UserAccountingDataCacheEntry>> factory, CancellationToken cancellationToken = default)
    {
        key = key.Normalize();
        var request = (key, startDate.Date, endDate.Date);
        InflightLoad load;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing) && existing.Entry.Coverage.Any(c => c.Contains(startDate, endDate))) { Touch(existing.Node); return existing.Entry; }
            if (!_inflight.TryGetValue(request, out load!))
            {
                load = new InflightLoad { Key = request };
                _inflight[request] = load;
                load.Task = LoadAndMergeAsync(request, load, key, startDate.Date, endDate.Date, factory);
            }
        }
        return await load.Task.WaitAsync(cancellationToken);
    }

    public void RemoveUser(string userId) { lock (_sync) { foreach (var key in _entries.Keys.Where(k => string.Equals(k.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList()) RemoveCore(key); CancelInflight(load => string.Equals(load.Key.Key.UserId, userId, StringComparison.OrdinalIgnoreCase)); } }
    public void RemoveStaleGenerations(long currentGeneration) { lock (_sync) { foreach (var key in _entries.Keys.Where(k => k.AccountingGeneration != currentGeneration).ToList()) RemoveCore(key); CancelInflight(load => load.Key.Key.AccountingGeneration != currentGeneration); } }
    public void InvalidateAll() { lock (_sync) { _entries.Clear(); _lru.Clear(); CancelInflight(_ => true); } }

    private async Task<UserAccountingDataCacheEntry> LoadAndMergeAsync((UserAccountingDataCacheKey Key, DateTime Start, DateTime End) request, InflightLoad load, UserAccountingDataCacheKey key, DateTime startDate, DateTime endDate, Func<CancellationToken, Task<UserAccountingDataCacheEntry>> factory)
    {
        try
        {
            var loaded = await factory(load.CancellationTokenSource.Token);
            lock (_sync)
            {
                if (!_inflight.TryGetValue(request, out var current) || !ReferenceEquals(current, load))
                    return loaded;
                if (_entries.TryGetValue(key, out var existing))
                {
                    var merged = new UserAccountingDataCacheEntry(key, MergeCoverage(existing.Entry.Coverage, new UserFactCoverage(startDate, endDate)), existing.Entry.Transactions.Concat(loaded.Transactions).GroupBy(t => t.Id).Select(g => g.First()), DateTime.UtcNow);
                    _entries[key] = (merged, existing.Node); Touch(existing.Node); return merged;
                }
                var node = _lru.AddFirst(key);
                _entries[key] = (new UserAccountingDataCacheEntry(key, [new UserFactCoverage(startDate, endDate)], loaded.Transactions, DateTime.UtcNow), node);
                while (_entries.Count > MaxKeys && _lru.Last is not null) RemoveCore(_lru.Last.Value);
                return _entries[key].Entry;
            }
        }
        finally
        {
            lock (_sync)
            {
                if (_inflight.TryGetValue(request, out var current) && ReferenceEquals(current, load))
                    _inflight.Remove(request);
            }
        }
    }

    private void CancelInflight(Func<InflightLoad, bool> predicate)
    {
        foreach (var request in _inflight.Where(pair => predicate(pair.Value)).Select(pair => pair.Key).ToList())
        {
            var load = _inflight[request];
            _inflight.Remove(request);
            load.CancellationTokenSource.Cancel();
        }
    }

    private sealed class InflightLoad
    {
        public CancellationTokenSource CancellationTokenSource { get; } = new();
        public Task<UserAccountingDataCacheEntry> Task { get; set; } = null!;
        public (UserAccountingDataCacheKey Key, DateTime Start, DateTime End) Key { get; set; }
    }

    private static IReadOnlyList<UserFactCoverage> MergeCoverage(IEnumerable<UserFactCoverage> existing, UserFactCoverage added)
    {
        var ranges = existing.Append(added).Select(c => c.Normalize()).OrderBy(c => c.StartDate).ToList();
        var merged = new List<UserFactCoverage>();
        foreach (var range in ranges)
        {
            if (merged.Count == 0 || range.StartDate > merged[^1].EndDate.AddDays(1)) merged.Add(range);
            else merged[^1] = new UserFactCoverage(merged[^1].StartDate, range.EndDate > merged[^1].EndDate ? range.EndDate : merged[^1].EndDate);
        }
        return merged.Count <= MaxCoverageSegments ? merged : merged.OrderByDescending(c => (c.EndDate - c.StartDate).Days).Take(MaxCoverageSegments).OrderBy(c => c.StartDate).ToArray();
    }

    private void Touch(LinkedListNode<UserAccountingDataCacheKey> node) { _lru.Remove(node); _lru.AddFirst(node); }
    private void RemoveCore(UserAccountingDataCacheKey key) { if (_entries.Remove(key, out var value)) _lru.Remove(value.Node); }
}

public sealed class UserScopedCacheInvalidator : IUserScopedCacheInvalidator
{
    private readonly IUserDonationDataCache _donations;
    private readonly IUserAccountingDataCache _accounting;

    public UserScopedCacheInvalidator(IUserDonationDataCache donations, IUserAccountingDataCache accounting)
    {
        _donations = donations;
        _accounting = accounting;
    }

    public void RemoveUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        _donations.RemoveUser(userId);
        _accounting.RemoveUser(userId);
    }
}
