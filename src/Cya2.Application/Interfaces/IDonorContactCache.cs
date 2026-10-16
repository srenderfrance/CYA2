using Cya2.Core.ReadModels;

namespace Cya2.Application.Interfaces;

public interface IDonorContactCache
{
    bool TryGet(long donorId, long generation, out IReadOnlyList<DonorContactRecord> contacts);
    void Set(long donorId, long generation, IEnumerable<DonorContactRecord> contacts);
    void InvalidateAll();
}
