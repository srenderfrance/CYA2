using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class AccountSnapshotCache : IAccountSnapshotCache
{
    private const int MaxEntries = 64;
    private const long MaxBytes = 64L * 1024 * 1024;

    private readonly object _sync = new();
    private readonly Dictionary<AccountSnapshotKey, CacheEntry> _entries = new();
    private readonly LinkedList<AccountSnapshotKey> _lru = new();
    private readonly Dictionary<AccountSnapshotKey, Lazy<Task<AccountDataSnapshot>>> _inflight = new();
    private readonly ILogger<AccountSnapshotCache> _logger;
    private CancellationTokenSource _invalidationCts = new();
    private long _currentBytes;

    public AccountSnapshotCache(ILogger<AccountSnapshotCache> logger)
    {
        _logger = logger;
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    public async Task<AccountDataSnapshot> GetOrCreateAsync(
        AccountSnapshotKey key,
        Func<CancellationToken, Task<AccountDataSnapshot>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        key = key.Normalize();
        if (TryGet(key, out var cached))
        {
            _logger.LogInformation(
                "Account snapshot cache hit: accountId={AccountId}, fund={Fund}, generation={Generation}, donations={DonationCount}, accounting={AccountingCount}, subAccounts={SubAccountCount}",
                key.AccountId,
                key.Fund,
                key.Generation,
                cached.Donations.Count,
                cached.Accounting.Count,
                cached.SubAccounts.Count);
            return cached;
        }

        Lazy<Task<AccountDataSnapshot>> lazy;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                Touch(existing);
                return existing.Snapshot;
            }

            if (!_inflight.TryGetValue(key, out lazy!))
            {
                _logger.LogInformation(
                    "Account snapshot cache miss: accountId={AccountId}, fund={Fund}, generation={Generation}, reason=missing-snapshot; loading from repository",
                    key.AccountId,
                    key.Fund,
                    key.Generation);
                var invalidationToken = _invalidationCts.Token;
                lazy = new Lazy<Task<AccountDataSnapshot>>(
                    () => LoadAndStoreAsync(key, factory, invalidationToken),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _inflight[key] = lazy;
            }
        }

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (_sync)
            {
                if (_inflight.TryGetValue(key, out var current) && ReferenceEquals(current, lazy))
                {
                    _inflight.Remove(key);
                }
            }
        }
    }

    public bool TryGet(AccountSnapshotKey key, out AccountDataSnapshot snapshot)
    {
        key = key.Normalize();

        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                Touch(entry);
                snapshot = entry.Snapshot;
                return true;
            }
        }

        snapshot = default!;
        return false;
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _entries.Clear();
            _lru.Clear();
            _currentBytes = 0;
            _invalidationCts.Cancel();
            _invalidationCts.Dispose();
            _invalidationCts = new CancellationTokenSource();
        }

        _logger.LogInformation("Account snapshot cache invalidated");
    }

    private async Task<AccountDataSnapshot> LoadAndStoreAsync(
        AccountSnapshotKey key,
        Func<CancellationToken, Task<AccountDataSnapshot>> factory,
        CancellationToken invalidationToken)
    {
        var snapshot = await factory(invalidationToken);
        ArgumentNullException.ThrowIfNull(snapshot);
        invalidationToken.ThrowIfCancellationRequested();

        if (snapshot.Key.Normalize() != key)
        {
            throw new InvalidOperationException("Account snapshot factory returned a snapshot for a different key.");
        }

        var entry = new CacheEntry(snapshot);
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var previous))
            {
                _currentBytes -= previous.Snapshot.ApproximateBytes;
                _lru.Remove(previous.Node);
            }

            entry.Node = _lru.AddFirst(key);
            _entries[key] = entry;
            _currentBytes += snapshot.ApproximateBytes;
            EvictIfNeeded();
        }

        _logger.LogInformation(
            "Account snapshot cache set: accountId={AccountId}, fund={Fund}, generation={Generation}, donations={DonationCount}, accounting={AccountingCount}, subAccounts={SubAccountCount}, approxBytes={ApproxBytes}",
            key.AccountId,
            key.Fund,
            key.Generation,
            snapshot.Donations.Count,
            snapshot.Accounting.Count,
            snapshot.SubAccounts.Count,
            snapshot.ApproximateBytes);

        return snapshot;
    }

    private void Touch(CacheEntry entry)
    {
        _lru.Remove(entry.Node);
        entry.Node = _lru.AddFirst(entry.Snapshot.Key.Normalize());
    }

    private void EvictIfNeeded()
    {
        while (_entries.Count > MaxEntries || _currentBytes > MaxBytes)
        {
            var node = _lru.Last;
            if (node is null)
            {
                break;
            }

            _lru.Remove(node);
            if (_entries.Remove(node.Value, out var entry))
            {
                _currentBytes -= entry.Snapshot.ApproximateBytes;
                _logger.LogDebug(
                    "Account snapshot cache evicted: accountId={AccountId}, fund={Fund}, generation={Generation}",
                    node.Value.AccountId,
                    node.Value.Fund,
                    node.Value.Generation);
            }
        }
    }

    private sealed class CacheEntry
    {
        public CacheEntry(AccountDataSnapshot snapshot)
        {
            Snapshot = snapshot;
            Node = null!;
        }

        public AccountDataSnapshot Snapshot { get; }
        public LinkedListNode<AccountSnapshotKey> Node { get; set; }
    }
}
