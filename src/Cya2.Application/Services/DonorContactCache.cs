using Cya2.Application.Interfaces;
using Cya2.Core.ReadModels;

namespace Cya2.Application.Services;

public sealed class DonorContactCache : IDonorContactCache
{
    private readonly object _sync = new();
    private readonly Dictionary<ContactKey, IReadOnlyList<DonorContactRecord>> _entries = new();

    public bool TryGet(long donorId, long generation, out IReadOnlyList<DonorContactRecord> contacts)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(new ContactKey(donorId, generation), out var cached))
            {
                contacts = cached;
                return true;
            }
        }

        contacts = Array.Empty<DonorContactRecord>();
        return false;
    }

    public void Set(long donorId, long generation, IEnumerable<DonorContactRecord> contacts)
    {
        var snapshot = (contacts ?? Enumerable.Empty<DonorContactRecord>()).ToArray();
        lock (_sync)
        {
            _entries[new ContactKey(donorId, generation)] = snapshot;
        }
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }

    private readonly record struct ContactKey(long DonorId, long Generation);
}
