using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace cya2.Services
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

        // Add this property and backing field
        private bool _bypassMonitoring = false;
        public bool BypassMonitoring => _bypassMonitoring;

        // Public properties now return our privately maintained state
        public bool IsConnected => _isConnected;
        public string LastError => _lastError;

        // Add this property to DatabaseMonitorService class
        public bool AllowMySqlOperations => cya2.GlobalSettings.AllowMySqlLoading;

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
            _suspended = false;

            // TEMPORARY: Comment out aggressive first-chance exception handling during debugging
            /*
            AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
            {
                try
                {
                    if (args.Exception != null && 
                        (args.Exception.ToString().Contains("MySql") || 
                         args.Exception.ToString().Contains("Timeout")))
                    {
                        // Log and mark database as disconnected immediately
                        _logger.LogWarning("First-chance MySQL exception: {Message}", args.Exception.Message);
                        _isConnected = false;
                        GlobalSettings.AllowMySqlLoading = false;
                        
                        // Trigger event on separate thread to avoid deadlocks
                        Task.Run(() => OnConnectionStatusChanged(false));
                    }
                }
                catch
                {
                    // Never throw from exception handlers
                }
            };
            */
            
            _logger.LogInformation("DatabaseMonitorService initialized - monitoring enabled");
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

        private static bool IsTransientMySqlTimeout(Exception ex)
        {
            if (ex is MySql.Data.MySqlClient.MySqlException my)
            {
                if (my.Message.Contains("Timeout expired", StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (ex is TimeoutException) return true;
            if (ex.InnerException is TimeoutException) return true;
            return false;
        }

        private void MonitorLoop()
        {
            try
            {
                // TEMPORARY: Skip monitoring entirely when in complete bypass mode
                // TODO: Remove this condition after Azure testing

                // Initial wait to let the application stabilize
                Thread.Sleep(5000);

                while (!_stopRequested)
                {
                    try
                    {
                        // Only perform checks when not suspended
                        if (!_suspended && !_bypassMonitoring)
                        {
                            bool newConnectionState;
                            
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
                            newConnectionState = cya2.GlobalSettings.CheckDatabaseTcpConnection(host, port, 2000);
                            
                            // If TCP check succeeds but MySQL operations are disabled, re-enable them
                            if (newConnectionState && !cya2.GlobalSettings.AllowMySqlLoading)
                            {
                                _logger.LogInformation("Database is now available - enabling MySQL operations");
                                cya2.GlobalSettings.AllowMySqlLoading = true;
                            }
                            
                            // IMPORTANT: Do not perform periodic MySQL Open checks here.
                            // These checks can create unobserved task exceptions and crash the process when the server is slow/unreachable.
                            // TCP reachability is sufficient for gating UI and allowing SafeDataAccess to attempt real operations.
                            // If TCP is reachable, enable MySQL operations. If not, disable.
                            cya2.GlobalSettings.AllowMySqlLoading = newConnectionState;
 
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
                cya2.GlobalSettings.AllowMySqlLoading = isConnected;

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

        // Add the InvokeAsync method
       // public async Task InvokeAsync(HttpContext context, IDataAccess dataAccess)
     //   {
            // TEMPORARY: Skip database checks in complete bypass mode
            // TODO: Remove this condition after Azure testing
          //  if (GlobalSettings.CompleteBypass)
         //   {
         //       await _next(context);
         //       return;
        //    }

            // Rest of the original method...
     //   }

        public void MarkAsDisconnected(string reason)
        {
            _lastError = reason;
            _isConnected = false;
            GlobalSettings.AllowMySqlLoading = false;
            
            _logger.LogWarning("Database marked as disconnected: {Reason}", reason);
            
            // Notify listeners
            OnConnectionStatusChanged(false);
        }
    }
 }