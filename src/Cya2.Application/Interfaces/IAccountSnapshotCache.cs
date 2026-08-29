using Cya2.Application.Models;

namespace Cya2.Application.Interfaces;

public interface IAccountSnapshotCache
{
    Task<AccountDataSnapshot> GetOrCreateAsync(
        AccountSnapshotKey key,
        Func<CancellationToken, Task<AccountDataSnapshot>> factory,
        CancellationToken cancellationToken = default);

    bool TryGet(AccountSnapshotKey key, out AccountDataSnapshot snapshot);

    bool Remove(AccountSnapshotKey key);

    void InvalidateAll();

    int Count { get; }
}
