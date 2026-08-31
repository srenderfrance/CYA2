using Cya2.Core.Interfaces;

namespace Cya2.Infrastructure.Services;

/// <summary>
/// Bridges <see cref="IDatabaseAvailabilityMonitor"/> to <see cref="IDatabaseGuard"/>
/// so that database adapters can guard calls without referencing the delivery host.
/// </summary>
public sealed class DatabaseGuardAdapter : IDatabaseGuard
{
    private readonly IDatabaseAvailabilityMonitor _monitor;

    public DatabaseGuardAdapter(IDatabaseAvailabilityMonitor monitor)
    {
        _monitor = monitor;
    }

    public bool IsAvailable => _monitor.IsConnected && _monitor.AllowMySqlOperations;

    public void ThrowIfUnavailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                $"Database is currently unavailable: {_monitor.LastError}");
        }
    }
}
