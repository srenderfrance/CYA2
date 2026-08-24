using Cya2.Application.Models;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Interfaces;

public interface IAccountSnapshotLoader
{
    Task<AccountDataSnapshot> LoadAsync(
        UserAccountContextAccount account,
        Cya2.Core.ValueObjects.DateRange queryRange,
        AccountSnapshotKey key,
        CancellationToken cancellationToken = default);
}
