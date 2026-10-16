using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;

namespace Cya2.Application.Services;

public sealed class DonorSummaryCache : IDonorSummaryCache
{
    private const int MaxEntries = 256;
    private readonly object _sync = new();
    private readonly Dictionary<SummaryKey, List<DonorSummaryDto>> _entries = new();
    private readonly LinkedList<SummaryKey> _lru = new();
    private readonly Dictionary<SummaryKey, LinkedListNode<SummaryKey>> _nodes = new();

    public bool TryGet(string scope, DateTime startDate, DateTime endDate, long donationsGeneration, long donorContactsGeneration, long authorizationGeneration, out List<DonorSummaryDto> data)
    {
        var key = NormalizeKey(scope, startDate, endDate, donationsGeneration, donorContactsGeneration, authorizationGeneration);
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                Touch(key);
                data = cached.ToList();
                return true;
            }
        }

        data = [];
        return false;
    }

    public void Set(string scope, DateTime startDate, DateTime endDate, long donationsGeneration, long donorContactsGeneration, long authorizationGeneration, List<DonorSummaryDto> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var key = NormalizeKey(scope, startDate, endDate, donationsGeneration, donorContactsGeneration, authorizationGeneration);
        lock (_sync)
        {
            _entries[key] = data.ToList();
            Touch(key);
            EvictIfNeeded();
        }
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _entries.Clear();
            _lru.Clear();
            _nodes.Clear();
        }
    }

    private void Touch(SummaryKey key)
    {
        if (_nodes.TryGetValue(key, out var node)) _lru.Remove(node);
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

    private static SummaryKey NormalizeKey(string scope, DateTime startDate, DateTime endDate, long donationsGeneration, long donorContactsGeneration, long authorizationGeneration)
        => new(scope?.Trim() ?? string.Empty, startDate.Date, endDate.Date, donationsGeneration, donorContactsGeneration, authorizationGeneration);

    private readonly record struct SummaryKey(string Scope, DateTime StartDate, DateTime EndDate, long DonationsGeneration, long DonorContactsGeneration, long AuthorizationGeneration);
}
