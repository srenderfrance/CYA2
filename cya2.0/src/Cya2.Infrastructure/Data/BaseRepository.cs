using DataLibrary; // Main application's IDataAccess
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System.Data;

namespace Cya2.Infrastructure.Data;

public abstract class BaseRepository
{
    protected readonly IDataAccess _dataAccess;
    protected readonly IConfiguration _configuration;
    protected readonly ILogger _logger;
    protected readonly string _connectionString;

    protected BaseRepository(IDataAccess dataAccess, IConfiguration configuration, ILogger logger)
    {
        _dataAccess = dataAccess;
        _configuration = configuration;
        _logger = logger;
        _connectionString = _configuration.GetConnectionString("default") 
            ?? throw new InvalidOperationException("Database connection string not found");
    }

    protected async Task<IDbConnection> CreateConnectionAsync()
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    protected async Task<T> ExecuteWithRetryAsync<T>(Func<IDbConnection, Task<T>> operation)
    {
        const int maxRetries = 3;
        var retryDelay = TimeSpan.FromMilliseconds(500);

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                var result = await operation(connection);
                return result;
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "Database operation failed on attempt {Attempt}. Retrying in {Delay}ms", 
                    attempt + 1, retryDelay.TotalMilliseconds);
                await Task.Delay(retryDelay);
                retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2); // Exponential backoff
            }
        }

        // If we reach here, all retries failed
        throw new InvalidOperationException($"Database operation failed after {maxRetries} attempts");
    }

    /// <summary>
    /// Load data using the main application's IDataAccess
    /// </summary>
    protected async Task<List<T>> LoadDataAsync<T, U>(string sql, U parameters)
    {
        try
        {
            return await _dataAccess.LoadData<T, U>(sql, parameters, _connectionString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading data with SQL: {Sql}", sql);
            throw;
        }
    }

    /// <summary>
    /// Save data using the main application's IDataAccess
    /// </summary>
    protected async Task<int> SaveDataAsync<T>(string sql, T parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dataAccess.SaveData(sql, parameters, _connectionString, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving data with SQL: {Sql}", sql);
            throw;
        }
    }

    /// <summary>
    /// Execute query and return first result or default using Dapper directly
    /// </summary>
    protected async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters);
        });
    }

    /// <summary>
    /// Execute query and return all results using Dapper directly
    /// </summary>
    protected async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.QueryAsync<T>(sql, parameters);
        });
    }

    /// <summary>
    /// Execute command and return affected rows using Dapper directly
    /// </summary>
    protected async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        return await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.ExecuteAsync(sql, parameters);
        });
    }

    /// <summary>
    /// Check if the database connection is healthy
    /// </summary>
    protected async Task<bool> IsConnectionHealthyAsync()
    {
        try
        {
            return await _dataAccess.CheckConnection(_connectionString);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database connection health check failed");
            return false;
        }
    }
}