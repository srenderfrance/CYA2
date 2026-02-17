using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System.Data;
using Dapper;

namespace Cya2.Infrastructure.Data;

/// <summary>
/// Enhanced data access service that extends the main application's IDataAccess
/// with additional functionality needed for clean architecture
/// </summary>
public class EnhancedDataAccess : IDataAccess
{
    private readonly IDataAccess _dataAccess;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EnhancedDataAccess> _logger;

    public EnhancedDataAccess(IDataAccess dataAccess, IConfiguration configuration, ILogger<EnhancedDataAccess> logger)
    {
        _dataAccess = dataAccess;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConnected => _dataAccess.IsConnected;
    public string LastError => _dataAccess.LastError;

    public async Task<List<T>> LoadData<T, U>(string sql, U parameters, string connectionString)
    {
        return await _dataAccess.LoadData<T, U>(sql, parameters, connectionString);
    }

    public async Task<int> SaveData<T>(string sql, T parameters, string connectionString, CancellationToken cancellationToken = default)
    {
        return await _dataAccess.SaveData(sql, parameters, connectionString, cancellationToken);
    }

    public async Task<bool> CheckConnection(string connectionString)
    {
        return await _dataAccess.CheckConnection(connectionString);
    }

    public bool ValidateConnectionString(string connectionString)
    {
        return _dataAccess.ValidateConnectionString(connectionString);
    }

    // Enhanced methods for clean architecture

    /// <summary>
    /// Execute with retry logic for enhanced reliability
    /// </summary>
    public async Task<T> ExecuteWithRetryAsync<T>(Func<IDbConnection, Task<T>> operation)
    {
        var connectionString = _configuration.GetConnectionString("default") ?? string.Empty;
        var maxRetries = 3;
        var retryDelay = TimeSpan.FromMilliseconds(500);

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();
                var result = await operation(connection);
                return result;
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "Database operation failed on attempt {Attempt}. Retrying in {Delay}ms", attempt + 1, retryDelay.TotalMilliseconds);
                await Task.Delay(retryDelay);
                retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2); // Exponential backoff
            }
        }

        // If we reach here, all retries failed
        throw new InvalidOperationException($"Database operation failed after {maxRetries} attempts");
    }

    /// <summary>
    /// Load single entity by ID with type safety
    /// </summary>
    public async Task<T?> LoadSingleAsync<T>(string sql, object parameters)
    {
        var connectionString = _configuration.GetConnectionString("default") ?? string.Empty;
        var results = await LoadData<T, object>(sql, parameters, connectionString);
        return results.FirstOrDefault();
    }

    /// <summary>
    /// Execute a query and return the first result or default
    /// </summary>
    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters);
        });
    }

    /// <summary>
    /// Execute a query and return all results
    /// </summary>
    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.QueryAsync<T>(sql, parameters);
        });
    }

    /// <summary>
    /// Execute a command and return affected rows
    /// </summary>
    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.ExecuteAsync(sql, parameters);
        });
    }

    /// <summary>
    /// Execute within a transaction
    /// </summary>
    public async Task<T> ExecuteInTransactionAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> operation)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var result = await operation(connection, transaction);
                await transaction.CommitAsync();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    /// <summary>
    /// Get connection string from configuration
    /// </summary>
    public string GetConnectionString()
    {
        return _configuration.GetConnectionString("default") ?? string.Empty;
    }
}