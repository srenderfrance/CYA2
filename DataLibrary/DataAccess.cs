using System.Data;
using System.Data.Common;
using Dapper;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Logging;

namespace DataLibrary
{
    public class DataAccess : IDataAccess
    {
        private readonly ILogger<DataAccess>? _logger;
        
        // Properties to track connection status
        public bool IsConnected { get; private set; } = true;
        public string LastError { get; private set; } = string.Empty;

        // Constructor with logger dependency
        public DataAccess(ILogger<DataAccess>? logger = null)
        {
            _logger = logger;
        }
        
        // Log helper method
        private void LogError(string message, Exception? ex = null)
        {
            LastError = message;
            if (_logger != null)
            {
                if (ex != null)
                    _logger.LogError(ex, message);
                else
                    _logger.LogError(message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }

        public async Task<List<T>> LoadData<T, U>(string sql, U parameters, string connectionString)
        {
            try
            {
                // Set a shorter timeout in the connection string
                MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder(connectionString);
                builder.ConnectionTimeout = 5; // 5 seconds timeout
                
                // Create a safer command definition without cancellation token
                using (IDbConnection connection = new MySqlConnection(builder.ConnectionString))
                {
                    // IMPORTANT: Don't use CancellationTokenSource, just use command timeout
                    var command = new CommandDefinition(
                        sql,
                        parameters,
                        commandTimeout: 5); // 5 seconds timeout without cancellation token
                        
                    // Use Task.WhenAny with a timeout task for safety
                    var queryTask = connection.QueryAsync<T>(command);
                    var timeoutTask = Task.Delay(6000); // 6 seconds (slightly longer than command timeout)
                    
                    var completedTask = await Task.WhenAny(queryTask, timeoutTask);
                    if (completedTask == queryTask)
                    {
                        var rows = await queryTask;
                        IsConnected = true;
                        LastError = string.Empty;
                        return rows.ToList();
                    }
                    else
                    {
                        // Timeout occurred
                        IsConnected = false;
                        LastError = "Query timed out";
                        return new List<T>();
                    }
                }
            }
            catch (MySqlException ex) when (
                ex.Number == 1042 || // Unable to connect to any MySQL server
                ex.Number == 1045 || // Access denied
                ex.Number == 2003 || // Cannot connect to MySQL
                ex.Message.Contains("Timeout expired") || // Timeout
                ex.InnerException is TimeoutException) // Nested timeout
            {
                // Connection-specific errors
                IsConnected = false;
                LastError = $"Database connection error: {ex.Message}";
                Console.WriteLine(LastError);
                
                // Return empty result instead of throwing
                return new List<T>();
            }
            catch (TimeoutException ex)
            {
                IsConnected = false;
                LastError = $"Database timeout: {ex.Message}";
                Console.WriteLine(LastError);
                return new List<T>();
            }
            catch (OperationCanceledException ex)
            {
                IsConnected = false;
                LastError = $"Database operation cancelled: {ex.Message}";
                Console.WriteLine(LastError);
                return new List<T>();
            }
            catch (Exception ex)
            {
                // Other errors
                IsConnected = false;
                LastError = $"Database error: {ex.Message}";
                Console.WriteLine(LastError);
                
                // Return empty result instead of throwing
                return new List<T>();
            }
        }

        public async Task<int> SaveData<T>(string sql, T parameters, string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                // Set a shorter timeout in the connection string
                MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder(connectionString);
                builder.ConnectionTimeout = 5; // 5 seconds timeout
                
                using (IDbConnection connection = new MySqlConnection(builder.ConnectionString))
                {
                    // IMPORTANT: Only use commandTimeout, not cancellation token
                    var command = new CommandDefinition(
                        sql,
                        parameters,
                        commandTimeout: 5); // 5 seconds, no cancellation token
                    
                    // Use Task.WhenAny with a timeout task for safety
                    var executeTask = connection.ExecuteAsync(command);
                    var timeoutTask = Task.Delay(6000); // 6 seconds (slightly longer than command timeout)
                    
                    var completedTask = await Task.WhenAny(executeTask, timeoutTask);
                    if (completedTask == executeTask)
                    {
                        var result = await executeTask;
                        IsConnected = true;
                        LastError = string.Empty;
                        return result;
                    }
                    else
                    {
                        // Timeout occurred
                        IsConnected = false;
                        LastError = "Database operation timed out";
                        return 0;
                    }
                }
            }
            catch (MySqlException ex) when (
                ex.Number == 1042 || // Unable to connect to any MySQL server
                ex.Number == 1045 || // Access denied
                ex.Number == 2003 || // Cannot connect to MySQL
                ex.Message.Contains("Timeout expired") || // Timeout
                ex.InnerException is TimeoutException) // Nested timeout
            {
                // Connection-specific errors
                IsConnected = false;
                LastError = $"Database connection error: {ex.Message}";
                Console.WriteLine(LastError);
                return 0;
            }
            catch (TimeoutException ex)
            {
                IsConnected = false;
                LastError = $"Database timeout: {ex.Message}";
                Console.WriteLine(LastError);
                return 0;
            }
            catch (OperationCanceledException ex)
            {
                IsConnected = false;
                LastError = $"Database operation cancelled: {ex.Message}";
                Console.WriteLine(LastError);
                return 0;
            }
            catch (Exception ex)
            {
                // Other errors
                IsConnected = false;
                LastError = $"Database error: {ex.Message}";
                Console.WriteLine(LastError);
                return 0;
            }
        }

        public async Task<bool> CheckConnection(string connectionString)
        {
            connectionString = EnsureConnectionParameters(connectionString);
            
            if (!ValidateConnectionString(connectionString))
            {
                IsConnected = false;
                return false;
            }
            
            try
            {
                // Simple and direct check with proper timeout
                var builder = new MySqlConnectionStringBuilder(connectionString);
                builder.ConnectionTimeout = 2; // 2 seconds timeout
                
                using (var connection = new MySqlConnection(builder.ConnectionString))
                {
                    await connection.OpenAsync();
                    await connection.CloseAsync();
                    
                    // Update connected state
                    IsConnected = true;
                    LastError = string.Empty;
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Update connection state
                IsConnected = false;
                LastError = $"Database connection failed: {ex.Message}";
                return false;
            }
        }

        private async Task<bool> TryConnectSafelyAsync(string connectionString)
        {
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    // Open connection with ConfigureAwait(false) to avoid deadlocks
                    await connection.OpenAsync().ConfigureAwait(false);
                    await connection.CloseAsync().ConfigureAwait(false);
                    return true;
                }
            }
            catch (MySqlException)
            {
                return false;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool ValidateConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                LastError = "Database connection string is empty or null";
                IsConnected = false;
                Console.WriteLine(LastError);
                return false;
            }
            
            try
            {
                var builder = new MySqlConnectionStringBuilder(connectionString);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Invalid connection string format: {ex.Message}";
                IsConnected = false;
                Console.WriteLine(LastError);
                return false;
            }
        }

        public static string EnsureConnectionParameters(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return connectionString;
                
            try
            {
                var builder = new MySqlConnectionStringBuilder(connectionString);
                
                // Ensure SSL is properly configured
                if (!connectionString.Contains("SslMode=", StringComparison.OrdinalIgnoreCase))
                    builder.SslMode = MySqlSslMode.Required;
                    
                // Ensure public key retrieval is enabled
                if (!connectionString.Contains("AllowPublicKeyRetrieval=", StringComparison.OrdinalIgnoreCase))
                    builder["AllowPublicKeyRetrieval"] = true;
                    
                return builder.ConnectionString;
            }
            catch
            {
                // If we can't parse it, return the original
                return connectionString;
            }
        }
    }
}

