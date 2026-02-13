using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace DataLibrary
{
    public class SafeDataAccess : IDataAccess
    {
        private readonly IDataAccess _innerDataAccess;
        private readonly IDatabaseMonitor _dbMonitor; // Use the interface
        private readonly ILogger<SafeDataAccess> _logger;

        public SafeDataAccess(IDataAccess dataAccess, IDatabaseMonitor dbMonitor, ILogger<SafeDataAccess> logger)
        {
            _innerDataAccess = dataAccess;
            _dbMonitor = dbMonitor;
            _logger = logger;
        }

        public bool IsConnected => _dbMonitor.IsConnected && _innerDataAccess.IsConnected;
        
        public string LastError => _dbMonitor.IsConnected 
            ? _innerDataAccess.LastError 
            : "Database is currently unavailable";

        public async Task<List<T>> LoadData<T, U>(string sql, U parameters, string connectionString)
        {
            // If bypassing monitoring, go directly to inner data access
            if (_dbMonitor.BypassMonitoring)
            {
                _logger.LogInformation("Bypassing database monitoring - direct database access");
                return await _innerDataAccess.LoadData<T, U>(sql, parameters, connectionString);
            }
            
            // Normal path with monitoring
            return await ExecuteWithTimeoutProtection(
                () => _innerDataAccess.LoadData<T, U>(sql, parameters, connectionString),
                new List<T>()
            );
        }

        public async Task<int> SaveData<T>(string sql, T parameters, string connectionString, CancellationToken cancellationToken = default)
        {
            // If bypassing monitoring, go directly to inner data access
            if (_dbMonitor.BypassMonitoring)
            {
                _logger.LogInformation("Bypassing database monitoring - direct database access");
                return await _innerDataAccess.SaveData(sql, parameters, connectionString, cancellationToken);
            }
            
            return await ExecuteWithTimeoutProtection(
                () => _innerDataAccess.SaveData(sql, parameters, connectionString, cancellationToken),
                0
            );
        }

        public async Task<bool> CheckConnection(string connectionString)
        {
            if (!_innerDataAccess.ValidateConnectionString(connectionString))
            {
                return false;
            }
            
            try
            {
                // If bypassing monitoring, go directly to inner data access
                if (_dbMonitor.BypassMonitoring)
                {
                    _logger.LogInformation("Bypassing database monitoring - direct connection check");
                    return await _innerDataAccess.CheckConnection(connectionString);
                }
                
                // Don't create a real connection if monitor shows database is disconnected
                // Allow connection checks even when currently disconnected so the monitor can detect recovery.
                // Still block checks when MySQL operations are disabled.
                if (!_dbMonitor.AllowMySqlOperations)
                {
                    return false;
                }
                
                // Use the inner data access to check connection
                return await ExecuteWithTimeoutProtection(
                    () => _innerDataAccess.CheckConnection(connectionString),
                    false
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking database connection");
                return false;
            }
        }

        public bool ValidateConnectionString(string connectionString)
        {
            // Always allow connection string validation
            return _innerDataAccess.ValidateConnectionString(connectionString);
        }

        // Add timeout protection to any database operation
        private async Task<T> ExecuteWithTimeoutProtection<T>(Func<Task<T>> operation, T defaultValue)
        {
            // Don't even try if the database is already known to be unavailable
            if (!_dbMonitor.IsConnected || !_dbMonitor.AllowMySqlOperations)
            {
                _logger.LogWarning("Database operation blocked - database unavailable");
                return defaultValue;
            }
            
            try
            {
                // Execute with extended timeout protection for import operations
                var timeoutTask = Task.Delay(30000); // 30 second timeout (increased from 5)
                var operationTask = operation();
                
                var completedTask = await Task.WhenAny(operationTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    // Operation timed out - use the interface method instead of direct access
                    _logger.LogWarning("Database operation timed out after 30 seconds");
                    _dbMonitor.MarkAsDisconnected("Operation timed out");
                    return defaultValue;
                }
                
                return await operationTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in database operation");
                
                // Only mark as disconnected for actual connection-level errors
                if (IsConnectionLevelError(ex))
                {
                    _dbMonitor.MarkAsDisconnected(ex.Message);
                }
                
                return defaultValue;
            }
        }
        
        private static bool IsConnectionLevelError(Exception ex)
        {
            // Connection-level MySQL error codes that indicate server/network problems
            var connectionErrorCodes = new HashSet<int>
            {
                1042, // ER_BAD_HOST_ERROR - Can't get hostname address
                1043, // ER_HANDSHAKE_ERROR - Bad handshake
                2002, // CR_CONNECTION_ERROR - Can't connect to MySQL server
                2003, // CR_CONN_HOST_ERROR - Can't connect to MySQL server on '%s' (%d)
                2006, // CR_SERVER_GONE_ERROR - MySQL server has gone away
                2013, // CR_SERVER_LOST - Lost connection to MySQL server during query
                2055  // CR_SERVER_LOST_EXTENDED - Lost connection to MySQL server at '%s'
            };
            
            if (ex is MySqlException myEx)
            {
                if (connectionErrorCodes.Contains(myEx.Number)) return true;
                
                // Check for timeout-related error messages
                if (myEx.Message?.Contains("Timeout in IO operation", StringComparison.OrdinalIgnoreCase) == true) return true;
                if (myEx.Message?.Contains("Timeout expired", StringComparison.OrdinalIgnoreCase) == true) return true;
            }
            
            if (ex is TimeoutException) return true;
            if (ex.InnerException is System.Net.Sockets.SocketException) return true;
            
            return false;
        }
    }
}
