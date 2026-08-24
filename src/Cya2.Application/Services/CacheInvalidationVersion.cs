using Cya2.Application.Interfaces;
using System.Threading;

namespace Cya2.Application.Services;

public sealed class CacheInvalidationVersion : ICacheInvalidationVersion
{
    private long _current;

    public long Current => Interlocked.Read(ref _current);

    public long Invalidate() => Interlocked.Increment(ref _current);
}
