namespace Cya2.Application.Interfaces;

public interface ICacheDataVersionProvider
{
    Task<string> GetDataMarkerAsync(CancellationToken cancellationToken = default);
}
