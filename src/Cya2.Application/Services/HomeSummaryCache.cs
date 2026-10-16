using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class HomeSummaryCache : IHomeSummaryCache
{
    private const int MaxEntries = 256;
    private readonly object _sync = new();
    private readonly Dictionary<HomeSummaryCacheKey, CacheEntry> _entries = new();
    private readonly LinkedList<HomeSummaryCacheKey> _lru = new();
    private readonly Dictionary<HomeSummaryCacheKey, Lazy<Task<HomeSummaryCacheEntry>>> _inflight = new();
    private readonly ILogger<HomeSummaryCache> _logger;

    public HomeSummaryCache(ILogger<HomeSummaryCache> logger) => _logger = logger;

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

    public async Task<HomeSummaryCacheEntry> GetOrCreateAsync(
        HomeSummaryCacheKey key,
        Func<CancellationToken, Task<HomeSummaryCacheEntry>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        key = key.Normalize();
        if (TryGet(key, out var cached))
        {
            return cached;
        }

        Lazy<Task<HomeSummaryCacheEntry>> lazy;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                Touch(existing);
                return existing.Entry;
            }

            if (!_inflight.TryGetValue(key, out lazy!))
            {
                lazy = new Lazy<Task<HomeSummaryCacheEntry>>(
                    () => LoadAndStoreAsync(key, factory),
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

    public bool TryGet(HomeSummaryCacheKey key, out HomeSummaryCacheEntry entry)
    {
        key = key.Normalize();
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                Touch(cached);
                entry = cached.Entry;
                return true;
            }
        }

        entry = default!;
        return false;
    }

    public void RemoveStaleGenerations(long generation)
    {
        lock (_sync)
        {
            foreach (var key in _entries.Keys.Where(key => key.Generation != generation).ToArray())
            {
                RemoveEntry(key);
            }
        }
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _entries.Clear();
            _lru.Clear();
        }
    }

    private async Task<HomeSummaryCacheEntry> LoadAndStoreAsync(
        HomeSummaryCacheKey key,
        Func<CancellationToken, Task<HomeSummaryCacheEntry>> factory)
    {
        var entry = await factory(CancellationToken.None);
        ArgumentNullException.ThrowIfNull(entry);

        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var previous))
            {
                RemoveEntry(previous);
            }

            var node = _lru.AddFirst(key);
            _entries[key] = new CacheEntry(entry, node);
            while (_entries.Count > MaxEntries && _lru.Last is not null)
            {
                RemoveEntry(_lru.Last.Value);
            }
        }

        return entry;
    }

    private void Touch(CacheEntry entry)
    {
        _lru.Remove(entry.Node);
        entry.Node = _lru.AddFirst(entry.Node.Value);
    }

    private void RemoveEntry(HomeSummaryCacheKey key)
    {
        if (_entries.Remove(key, out var entry))
        {
            _lru.Remove(entry.Node);
        }
    }

    private void RemoveEntry(CacheEntry entry)
    {
        _entries.Remove(entry.Node.Value);
        _lru.Remove(entry.Node);
    }

    private sealed class CacheEntry(HomeSummaryCacheEntry entry, LinkedListNode<HomeSummaryCacheKey> node)
    {
        public HomeSummaryCacheEntry Entry { get; } = entry;
        public LinkedListNode<HomeSummaryCacheKey> Node { get; set; } = node;
    }
}
