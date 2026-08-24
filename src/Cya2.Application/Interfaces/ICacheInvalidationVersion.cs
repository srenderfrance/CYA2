namespace Cya2.Application.Interfaces;

public interface ICacheInvalidationVersion
{
    long Current { get; }
    long Invalidate();
}
