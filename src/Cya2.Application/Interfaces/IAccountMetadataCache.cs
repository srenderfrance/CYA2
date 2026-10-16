using Cya2.Application.Models;

namespace Cya2.Application.Interfaces;

public interface IAccountMetadataCache
{
    bool TryGet(int accountId, long generation, out AccountMetadataSnapshot metadata);
    void Set(int accountId, long generation, AccountMetadataSnapshot metadata);
    void InvalidateAll();
}
