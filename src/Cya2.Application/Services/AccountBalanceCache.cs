using Cya2.Application.Interfaces;

namespace Cya2.Application.Services;

public sealed class AccountBalanceCache : IAccountBalanceCache
{
    private const int MaxEntries = 256;
    private readonly object _sync = new();
    private readonly Dictionary<BalanceKey, decimal> _entries = new();
    private readonly LinkedList<BalanceKey> _lru = new();
    private readonly Dictionary<BalanceKey, LinkedListNode<BalanceKey>> _nodes = new();
    private long _generation = -1;

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

    public bool TryGet(
        int accountId,
        string accountingClass,
        string accountNumber,
        decimal balanceAdjustment,
        long generation,
        DateTime asOfDate,
        string userId,
        out decimal totalBalance)
    {
        var key = NormalizeKey(accountId, accountingClass, accountNumber, balanceAdjustment, generation, asOfDate, userId);
        lock (_sync)
        {
            if (_generation == generation && _entries.TryGetValue(key, out totalBalance))
            {
                Touch(key);
                return true;
            }
        }

        totalBalance = 0m;
        return false;
    }

    public void Set(
        int accountId,
        string accountingClass,
        string accountNumber,
        decimal balanceAdjustment,
        long generation,
        DateTime asOfDate,
        string userId,
        decimal totalBalance)
    {
        var key = NormalizeKey(accountId, accountingClass, accountNumber, balanceAdjustment, generation, asOfDate, userId);
        lock (_sync)
        {
            PrepareGeneration(generation);
            _entries[key] = totalBalance;
            Touch(key);
            EvictIfNeeded();
        }
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _generation = -1;
            _entries.Clear();
            _lru.Clear();
            _nodes.Clear();
        }
    }

    public void RemoveStaleGeneration(long currentGeneration)
    {
        lock (_sync)
        {
            var staleKeys = new List<BalanceKey>();
            foreach (var key in _entries.Keys)
            {
                if (key.Generation != currentGeneration)
                {
                    staleKeys.Add(key);
                }
            }

            foreach (var key in staleKeys)
            {
                _entries.Remove(key);
                if (_nodes.Remove(key, out var node))
                {
                    _lru.Remove(node);
                }
            }

            _generation = currentGeneration;
        }
    }

    private void PrepareGeneration(long generation)
    {
        if (_generation == generation)
        {
            return;
        }

        _generation = generation;
        _entries.Clear();
        _lru.Clear();
        _nodes.Clear();
    }

    private void Touch(BalanceKey key)
    {
        if (_nodes.TryGetValue(key, out var existing))
        {
            _lru.Remove(existing);
        }

        _nodes[key] = _lru.AddFirst(key);
    }

    private void EvictIfNeeded()
    {
        while (_entries.Count > MaxEntries && _lru.Last is not null)
        {
            var key = _lru.Last.Value;
            _lru.RemoveLast();
            _nodes.Remove(key);
            _entries.Remove(key);
        }
    }

    private static BalanceKey NormalizeKey(
        int accountId,
        string accountingClass,
        string accountNumber,
        decimal balanceAdjustment,
        long generation,
        DateTime asOfDate,
        string userId)
    {
        var normalizedAsOfDate = asOfDate.Date > DateTime.UtcNow.Date
            ? DateTime.UtcNow.Date
            : asOfDate.Date;

        return new(
            accountId,
            accountingClass?.Trim() ?? string.Empty,
            accountNumber?.Trim() ?? string.Empty,
            balanceAdjustment,
            generation,
            normalizedAsOfDate,
            userId?.Trim() ?? string.Empty);
    }

    private readonly record struct BalanceKey(
        int AccountId,
        string AccountingClass,
        string AccountNumber,
        decimal BalanceAdjustment,
        long Generation,
        DateTime AsOfDate,
        string UserId);
}
