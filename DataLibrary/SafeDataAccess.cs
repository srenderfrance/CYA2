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
                if (!_dbMonitor.IsConnected || !_dbMonitor.AllowMySqlOperations)
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
                // Execute with timeout protection
                var timeoutTask = Task.Delay(5000); // 5 second timeout
                var operationTask = operation();
                
                var completedTask = await Task.WhenAny(operationTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    // Operation timed out - use the interface method instead of direct access
                    _logger.LogWarning("Database operation timed out");
                    _dbMonitor.MarkAsDisconnected("Operation timed out");
                    return defaultValue;
                }
                
                return await operationTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in database operation");
                // Use the interface method instead of direct access
                _dbMonitor.MarkAsDisconnected(ex.Message);
                return defaultValue;
            }
        }
    }
}
