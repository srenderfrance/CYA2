using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class DonationDataCache : IDonationDataCache
{
    private const int MaxEntries = 64;
    private const long MaxBytes = 64L * 1024 * 1024;
    private readonly object _sync = new();
    private readonly Dictionary<DonationDataCacheKey, Entry> _entries = new();
    private readonly LinkedList<DonationDataCacheKey> _lru = new();
    private readonly Dictionary<DonationDataCacheKey, Lazy<Task<DonationDataCacheEntry>>> _inflight = new();
    private readonly ILogger<DonationDataCache> _logger;
    private long _currentBytes;

    public DonationDataCache(ILogger<DonationDataCache> logger) => _logger = logger;

    public async Task<DonationDataCacheEntry> GetOrCreateAsync(
        DonationDataCacheKey key,
        Func<CancellationToken, Task<DonationDataCacheEntry>> factory,
        CancellationToken cancellationToken = default)
    {
        key = key.Normalize();
        if (TryGet(key, out var cached)) return cached;

        Lazy<Task<DonationDataCacheEntry>> lazy;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                Touch(existing);
                return existing.Value;
            }

            if (!_inflight.TryGetValue(key, out lazy!))
            {
                lazy = new Lazy<Task<DonationDataCacheEntry>>(
                    () => LoadAndStoreAsync(key, factory),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _inflight[key] = lazy;
            }

        }

        try { return await lazy.Value.WaitAsync(cancellationToken); }
        finally
        {
            lock (_sync)
            {
                if (_inflight.TryGetValue(key, out var current) && ReferenceEquals(current, lazy))
                    _inflight.Remove(key);
            }
        }
    }

    public bool TryGet(DonationDataCacheKey key, out DonationDataCacheEntry entry)
    {
        key = key.Normalize();
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                Touch(cached);
                entry = cached.Value;
                return true;
            }
        }

        entry = default!;
        return false;
    }

    public void RemoveStaleGenerations(long currentDonationsGeneration)
    {
        lock (_sync)
        {
            foreach (var key in _entries.Keys.Where(k => k.DonationsGeneration != currentDonationsGeneration).ToList())
                RemoveCore(key);
        }
    }

    public void Remove(DonationDataCacheKey key)
    {
            lock (_sync) RemoveCore(key.Normalize());
    }

    public void RemoveAccount(int accountId)
    {
        lock (_sync)
        {
            foreach (var key in _entries.Keys.Where(k => k.AccountId == accountId).ToList())
                RemoveCore(key);
        }
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _entries.Clear();
            _lru.Clear();
            _currentBytes = 0;
        }
    }

    private async Task<DonationDataCacheEntry> LoadAndStoreAsync(
        DonationDataCacheKey key,
        Func<CancellationToken, Task<DonationDataCacheEntry>> factory)
    {
        var value = await factory(CancellationToken.None);
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                Touch(existing);
                return existing.Value;
            }

            var node = _lru.AddFirst(key);
            _entries[key] = new Entry(value, node, EstimateBytes(value));
            _currentBytes += _entries[key].Bytes;
            Evict();
        }
        return value;
    }

    private void Touch(Entry entry)
    {
        _lru.Remove(entry.Node);
        entry.Node = _lru.AddFirst(entry.Value.Key);
    }

    private void Evict()
    {
        while (_entries.Count > MaxEntries || _currentBytes > MaxBytes)
        {
            var node = _lru.Last;
            if (node is null) break;
            RemoveCore(node.Value);
        }
    }

    private void RemoveCore(DonationDataCacheKey key)
    {
        if (_entries.Remove(key, out var entry))
        {
            _lru.Remove(entry.Node);
            _currentBytes -= entry.Bytes;
        }
    }

    private static long EstimateBytes(DonationDataCacheEntry entry) =>
        256L + (entry.Donations.Count * 256L) + (entry.SubAccounts.Count * 128L);

    private sealed class Entry(DonationDataCacheEntry value, LinkedListNode<DonationDataCacheKey> node, long bytes)
    {
        public DonationDataCacheEntry Value { get; } = value;
        public LinkedListNode<DonationDataCacheKey> Node { get; set; } = node;
        public long Bytes { get; } = bytes;
    }
}

public sealed class AccountingDataCache : IAccountingDataCache
{
    private const int MaxEntries = 64;
    private const long MaxBytes = 64L * 1024 * 1024;
    private readonly object _sync = new();
    private readonly Dictionary<AccountingDataCacheKey, Entry> _entries = new();
    private readonly LinkedList<AccountingDataCacheKey> _lru = new();
    private readonly Dictionary<AccountingDataCacheKey, Lazy<Task<AccountingDataCacheEntry>>> _inflight = new();
    private readonly ILogger<AccountingDataCache> _logger;
    private long _currentBytes;

    public AccountingDataCache(ILogger<AccountingDataCache> logger) => _logger = logger;

    public async Task<AccountingDataCacheEntry> GetOrCreateAsync(
        AccountingDataCacheKey key,
        Func<CancellationToken, Task<AccountingDataCacheEntry>> factory,
        CancellationToken cancellationToken = default)
    {
        key = key.Normalize();
        if (TryGet(key, out var cached)) return cached;

        Lazy<Task<AccountingDataCacheEntry>> lazy;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                Touch(existing);
                return existing.Value;
            }

            if (!_inflight.TryGetValue(key, out lazy!))
            {
                lazy = new Lazy<Task<AccountingDataCacheEntry>>(
                    () => LoadAndStoreAsync(key, factory),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _inflight[key] = lazy;
            }
        }

        try { return await lazy.Value.WaitAsync(cancellationToken); }
        finally
        {
            lock (_sync)
            {
                if (_inflight.TryGetValue(key, out var current) && ReferenceEquals(current, lazy))
                    _inflight.Remove(key);
            }
        }
    }

    public bool TryGet(AccountingDataCacheKey key, out AccountingDataCacheEntry entry)
    {
        key = key.Normalize();
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                Touch(cached);
                entry = cached.Value;
                return true;
            }
        }

        entry = default!;
        return false;
    }

    public void RemoveStaleGenerations(long currentAccountingGeneration)
    {
        lock (_sync)
        {
            foreach (var key in _entries.Keys.Where(k => k.AccountingGeneration != currentAccountingGeneration).ToList())
                RemoveCore(key);
        }
    }

    public void Remove(AccountingDataCacheKey key)
    {
            lock (_sync) RemoveCore(key.Normalize());
    }

    public void RemoveAccount(int accountId)
    {
        lock (_sync)
        {
            foreach (var key in _entries.Keys.Where(k => k.AccountId == accountId).ToList())
                RemoveCore(key);
        }
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _entries.Clear();
            _lru.Clear();
            _currentBytes = 0;
        }
    }

    private async Task<AccountingDataCacheEntry> LoadAndStoreAsync(
        AccountingDataCacheKey key,
        Func<CancellationToken, Task<AccountingDataCacheEntry>> factory)
    {
        var value = await factory(CancellationToken.None);
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                Touch(existing);
                return existing.Value;
            }

            var node = _lru.AddFirst(key);
            _entries[key] = new Entry(value, node, EstimateBytes(value));
            _currentBytes += _entries[key].Bytes;
            Evict();
        }
        return value;
    }

    private void Touch(Entry entry)
    {
        _lru.Remove(entry.Node);
        entry.Node = _lru.AddFirst(entry.Value.Key);
    }

    private void Evict()
    {
        while (_entries.Count > MaxEntries || _currentBytes > MaxBytes)
        {
            var node = _lru.Last;
            if (node is null) break;
            RemoveCore(node.Value);
        }
    }

    private void RemoveCore(AccountingDataCacheKey key)
    {
        if (_entries.Remove(key, out var entry))
        {
            _lru.Remove(entry.Node);
            _currentBytes -= entry.Bytes;
        }
    }

    private static long EstimateBytes(AccountingDataCacheEntry entry) =>
        192L + (entry.Transactions.Count * 192L);

    private sealed class Entry(AccountingDataCacheEntry value, LinkedListNode<AccountingDataCacheKey> node, long bytes)
    {
        public AccountingDataCacheEntry Value { get; } = value;
        public LinkedListNode<AccountingDataCacheKey> Node { get; set; } = node;
        public long Bytes { get; } = bytes;
    }
}
