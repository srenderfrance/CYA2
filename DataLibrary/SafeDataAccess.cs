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
            if (!_dbMonitor.IsConnected)
            {
                _logger.LogWarning($"Database operation blocked (LoadData) - database unavailable");
                return new List<T>(); // Return empty list
            }
            
            return await _innerDataAccess.LoadData<T, U>(sql, parameters, connectionString);
        }

        public async Task<int> SaveData<T>(string sql, T parameters, string connectionString, CancellationToken cancellationToken = default)
        {
            if (!_dbMonitor.IsConnected)
            {
                _logger.LogWarning($"Database operation blocked (SaveData) - database unavailable");
                return 0;
            }
            
            return await _innerDataAccess.SaveData(sql, parameters, connectionString, cancellationToken);
        }

        public async Task<bool> CheckConnection(string connectionString)
        {
            if (!_innerDataAccess.ValidateConnectionString(connectionString))
            {
                return false;
            }
            
            try
            {
                // Don't create a real connection if monitor shows database is disconnected
                if (!_dbMonitor.IsConnected || !_dbMonitor.AllowMySqlOperations)
                {
                    return false;
                }
                
                // Use the inner data access to check connection
                return await _innerDataAccess.CheckConnection(connectionString);
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
    }
}
