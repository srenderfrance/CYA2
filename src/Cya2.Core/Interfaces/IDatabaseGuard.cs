namespace Cya2.Core.Interfaces;

/// <summary>
/// Minimal DB-availability contract that Infrastructure repositories use
/// to guard against MySQL calls when the database is unavailable.
/// Implemented by a thin adapter in the host project that forwards to
/// <c>IDatabaseAvailabilityMonitor</c>.
/// </summary>
public interface IDatabaseGuard
{
    /// <summary>True when the database is reachable and MySQL operations are permitted.</summary>
    bool IsAvailable { get; }

    /// <summary>Throws <see cref="InvalidOperationException"/> if the database is unavailable.</summary>
    void ThrowIfUnavailable();
}
