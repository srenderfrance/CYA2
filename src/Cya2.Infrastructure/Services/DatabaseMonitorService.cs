using Cya2.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace Cya2.Infrastructure.Services;

public sealed class DatabaseMonitorService : BackgroundService, IDatabaseAvailabilityMonitor
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseMonitorService> _logger;

    private volatile bool _stopRequested = false;
    private volatile bool _suspended = false;
    private volatile bool _bypassMonitoring = false;
    private volatile bool _isConnected = true;
    private string _lastError = string.Empty;

    public bool IsConnected => _isConnected;
    public string LastError => _lastError;
    public bool BypassMonitoring => _bypassMonitoring;
    public bool AllowMySqlOperations => _isConnected;

    public event EventHandler<bool>? ConnectionStatusChanged;

    public DatabaseMonitorService(IConfiguration configuration, ILogger<DatabaseMonitorService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _logger.LogInformation("DatabaseMonitorService initialized");
    }

    public void Suspend() => _suspended = true;
    public void Resume() => _suspended = false;

    public void SetBypassMonitoring(bool bypass)
    {
        _bypassMonitoring = bypass;
        _logger.LogWarning("Database monitoring bypass set to: {Status}", bypass ? "ENABLED" : "DISABLED");
    }

    public void MarkAsDisconnected(string reason)
    {
        _lastError = reason;
        _isConnected = false;
        _logger.LogWarning("Database marked as disconnected: {Reason}", reason);
        OnConnectionStatusChanged(false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var thread = new Thread(MonitorLoop) { IsBackground = true };
        thread.Start();
        stoppingToken.Register(() => _stopRequested = true);
        return Task.CompletedTask;
    }

    private void MonitorLoop()
    {
        Thread.Sleep(5000);

        while (!_stopRequested)
        {
            try
            {
                if (!_suspended && !_bypassMonitoring)
                {
                    var (host, port) = ParseHostPort();
                    var newState = CheckTcpConnection(host, port, timeoutMs: 2000);

                    if (_isConnected != newState)
                    {
                        _logger.LogInformation(
                            "Database connection state changed from {Old} to {New}",
                            _isConnected ? "connected" : "disconnected",
                            newState ? "connected" : "disconnected");

                        _isConnected = newState;
                        if (!newState) _lastError = "TCP connection failed";
                        else _lastError = string.Empty;

                        OnConnectionStatusChanged(newState);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DatabaseMonitorService monitor loop");
            }

            Thread.Sleep(5000);
        }
    }

    private (string host, int port) ParseHostPort()
    {
        var cs = _configuration.GetConnectionString("default") ?? string.Empty;
        string host = "localhost";
        int port = 3306;

        foreach (var part in cs.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("server=", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("host=", StringComparison.OrdinalIgnoreCase))
            {
                host = trimmed[(trimmed.IndexOf('=') + 1)..].Trim();
            }
            else if (trimmed.StartsWith("port=", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(trimmed[(trimmed.IndexOf('=') + 1)..].Trim(), out port);
            }
        }

        return (host, port);
    }

    private static bool CheckTcpConnection(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(timeoutMs);
            if (!success) return false;
            client.EndConnect(result);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnConnectionStatusChanged(bool isConnected)
    {
        try
        {
            ConnectionStatusChanged?.Invoke(this, isConnected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error raising ConnectionStatusChanged event");
        }
    }
}
