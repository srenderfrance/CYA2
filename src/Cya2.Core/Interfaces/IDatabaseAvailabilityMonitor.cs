namespace Cya2.Core.Interfaces;

/// <summary>
/// Monitors database availability and exposes connection state to the application.
/// Implemented by DatabaseMonitorService in Cya2.Infrastructure.
/// </summary>
public interface IDatabaseAvailabilityMonitor
{
    bool IsConnected { get; }
    string LastError { get; }
    bool BypassMonitoring { get; }
    bool AllowMySqlOperations { get; }
    event EventHandler<bool> ConnectionStatusChanged;
    void Suspend();
    void Resume();
    void SetBypassMonitoring(bool bypass);
    void MarkAsDisconnected(string reason);
}
