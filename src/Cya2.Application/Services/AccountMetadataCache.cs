using Cya2.Application.Interfaces;
using Cya2.Application.Models;

namespace Cya2.Application.Services;

public sealed class AccountMetadataCache : IAccountMetadataCache
{
    private readonly object _sync = new();
    private readonly Dictionary<MetadataKey, AccountMetadataSnapshot> _entries = new();

    public bool TryGet(int accountId, long generation, out AccountMetadataSnapshot metadata)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(new MetadataKey(accountId, generation), out var cached))
            {
                metadata = cached;
                return true;
            }
        }

        metadata = default!;
        return false;
    }

    public void Set(int accountId, long generation, AccountMetadataSnapshot metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        lock (_sync)
        {
            _entries[new MetadataKey(accountId, generation)] = metadata;
        }
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }

    private readonly record struct MetadataKey(int AccountId, long Generation);
}
