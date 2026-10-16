using Cya2.Core.Interfaces;

namespace Cya2.Application.Interfaces;

public interface ICacheInvalidationVersion
{
    long Current { get; }
    long Donations { get; }
    long Accounting { get; }
    long AccountMetadata { get; }
    long DonorContacts { get; }
    long Authorization { get; }
    long GetGeneration(CacheDataDomains domains);
    long Invalidate();
    long Invalidate(CacheDataDomains domains);
}
