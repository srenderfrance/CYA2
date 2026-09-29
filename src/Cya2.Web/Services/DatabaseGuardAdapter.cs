using Cya2.Core.Interfaces;

namespace cya2.Services;

/// <summary>
/// Bridges <see cref="IDatabaseAvailabilityMonitor"/> to <see cref="IDatabaseGuard"/>
/// so that Infrastructure repositories can guard DB calls without referencing host types.
/// </summary>
internal sealed class DatabaseGuardAdapter : IDatabaseGuard
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
            throw new InvalidOperationException(
                $"Database is currently unavailable: {_monitor.LastError}");
    }
}
