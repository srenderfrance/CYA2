using Cya2.Application.Interfaces;
using System.Threading;

namespace Cya2.Application.Services;

public sealed class CacheInvalidationVersion : ICacheInvalidationVersion
{
    private long _current;
    private long _donations;
    private long _accounting;
    private long _accountMetadata;
    private long _donorContacts;
    private long _authorization;

    public long Current => Interlocked.Read(ref _current);
    public long Donations => Interlocked.Read(ref _donations);
    public long Accounting => Interlocked.Read(ref _accounting);
    public long AccountMetadata => Interlocked.Read(ref _accountMetadata);
    public long DonorContacts => Interlocked.Read(ref _donorContacts);
    public long Authorization => Interlocked.Read(ref _authorization);

    public long GetGeneration(CacheDataDomains domains)
    {
        if (domains == CacheDataDomains.Donations) return Donations;
        if (domains == CacheDataDomains.Accounting) return Accounting;
        if (domains == CacheDataDomains.AccountMetadata) return AccountMetadata;
        if (domains == CacheDataDomains.DonorContacts) return DonorContacts;
        if (domains == CacheDataDomains.Authorization) return Authorization;
        return Current;
    }

    public long Invalidate() => Invalidate(CacheDataDomains.All);

    public long Invalidate(CacheDataDomains domains)
    {
        if (domains.HasFlag(CacheDataDomains.Donations)) Interlocked.Increment(ref _donations);
        if (domains.HasFlag(CacheDataDomains.Accounting)) Interlocked.Increment(ref _accounting);
        if (domains.HasFlag(CacheDataDomains.AccountMetadata)) Interlocked.Increment(ref _accountMetadata);
        if (domains.HasFlag(CacheDataDomains.DonorContacts)) Interlocked.Increment(ref _donorContacts);
        if (domains.HasFlag(CacheDataDomains.Authorization)) Interlocked.Increment(ref _authorization);
        return Interlocked.Increment(ref _current);
    }
}
