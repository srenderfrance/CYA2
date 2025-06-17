using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace cya2._0.Services
{
    public class DatabaseMonitorService : BackgroundService, DataLibrary.IDatabaseMonitor
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DatabaseMonitorService> _logger;
        private volatile bool _stopRequested = false;
        private volatile bool _suspended = false;
        private Thread _monitorThread;

        // Use backing fields for these properties since we can't access scoped services directly
        private bool _isConnected = true;
        private string _lastError = string.Empty;
        private int _consecutiveFailures = 0;
        private readonly int _maxConsecutiveFailures = 3; // After this many failures, add a longer delay
        private readonly int _backoffSeconds = 30; // Wait this long after multiple failures

        // Add this property and backing field
        private bool _bypassMonitoring = false;
        public bool BypassMonitoring => _bypassMonitoring;

        // Public properties now return our privately maintained state
        public bool IsConnected => _isConnected;
        public string LastError => _lastError;

        // Add this property to DatabaseMonitorService class
        public bool AllowMySqlOperations => cya2._0.GlobalSettings.AllowMySqlLoading;

        // Event for connection status changes
        public event EventHandler<bool> ConnectionStatusChanged;

        // Use IServiceProvider instead of injecting IDataAccess directly
        public DatabaseMonitorService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<DatabaseMonitorService> logger)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
            _suspended = true; // Start suspended
        }

        // Required to maintain API compatibility
        public void Suspend() => _suspended = true;
        public void Resume() => _suspended = false;

        // Add a method to toggle the bypass
        public void SetBypassMonitoring(bool bypass)
        {
            _bypassMonitoring = bypass;
            _logger.LogWarning("Database monitoring bypass set to: {Status}", 
                bypass ? "ENABLED" : "DISABLED");
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Start a background thread for database monitoring
            _monitorThread = new Thread(MonitorLoop);
            _monitorThread.IsBackground = true;
            _monitorThread.Start();

            stoppingToken.Register(() => _stopRequested = true);

            return Task.CompletedTask;
        }

        private void MonitorLoop()
        {
            try
            {
                // Initial wait to let the application stabilize
                Thread.Sleep(5000);

                while (!_stopRequested)
                {
                    try
                    {
                        // Only perform checks when not suspended
                        if (!_suspended && !_bypassMonitoring)
                        {
                            bool newConnectionState = false;
                            
                            // Always attempt a lightweight TCP check - this doesn't use MySQL assemblies
                            var connectionString = _configuration.GetConnectionString("default") ?? "";
                            string host = "localhost";
                            int port = 3306;
                            
                            // Extract host and port
                            foreach (var part in connectionString.Split(';'))
                            {
                                if (part.Trim().StartsWith("server=", StringComparison.OrdinalIgnoreCase) ||
                                    part.Trim().StartsWith("host=", StringComparison.OrdinalIgnoreCase))
                                {
                                    host = part.Substring(part.IndexOf('=') + 1).Trim();
                                }
                                else if (part.Trim().StartsWith("port=", StringComparison.OrdinalIgnoreCase))
                                {
                                    int.TryParse(part.Substring(part.IndexOf('=') + 1).Trim(), out port);
                                }
                            }
                            
                            // Use a TCP check that doesn't need MySQL assemblies
                            newConnectionState = cya2._0.GlobalSettings.CheckDatabaseTcpConnection(host, port, 2000);
                            
                            // If TCP check succeeds but MySQL operations are disabled, re-enable them
                            if (newConnectionState && !cya2._0.GlobalSettings.AllowMySqlLoading)
                            {
                                _logger.LogInformation("Database is now available - enabling MySQL operations");
                                cya2._0.GlobalSettings.AllowMySqlLoading = true;
                            }
                            
                            // Now do the full MySQL check only if allowed
                            if (cya2._0.GlobalSettings.AllowMySqlLoading)
                            {
                                using (var scope = _serviceProvider.CreateScope())
                                {
                                    try
                                    {
                                        var dataAccess = scope.ServiceProvider.GetRequiredService<IDataAccess>();
                                        
                                        // Use a safer check with timeout
                                        var checkTask = Task.Run(async () =>
                                            await dataAccess.CheckConnection(connectionString));

                                        if (checkTask.Wait(3000)) // 3 second timeout
                                        {
                                            newConnectionState = checkTask.Result;
                                        }
                                        else
                                        {
                                            _logger.LogWarning("Database connection check timed out");
                                            newConnectionState = false;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error checking database connection");
                                        newConnectionState = false;
                                    }
                                }
                            }
                            
                            // If connection state changes, update
                            if (_isConnected != newConnectionState)
                            {
                                _logger.LogInformation("Database connection state changed from {OldState} to {NewState}",
                                    _isConnected ? "connected" : "disconnected",
                                    newConnectionState ? "connected" : "disconnected");

                                _isConnected = newConnectionState;
                                OnConnectionStatusChanged(newConnectionState);
                            }
                        }
                        else if (_bypassMonitoring)
                        {
                            _logger.LogWarning("Database monitoring is bypassed - skipping check");
                        }
                        else
                        {
                            _logger.LogDebug("Database monitoring is suspended - skipping check");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in monitor loop");
                    }

                    // Sleep for a while before checking again
                    Thread.Sleep(5000); // Check every 5 seconds
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in database monitor thread");
            }
        }

        // Helper method to raise the event
        private void OnConnectionStatusChanged(bool isConnected)
        {
            try
            {
                // Update GlobalSettings when connection state changes
                cya2._0.GlobalSettings.AllowMySqlLoading = isConnected;

                _logger.LogInformation("Database connection state changed to {State}. MySQL operations are now {Action}.",
                    isConnected ? "connected" : "disconnected",
                    isConnected ? "enabled" : "disabled");

                ConnectionStatusChanged?.Invoke(this, isConnected);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error raising ConnectionStatusChanged event");
            }
        }
    }
}