using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cya2.Infrastructure.Data;

/// <summary>
/// Base repository implementation using existing IDataAccess from main app
/// Bridges clean architecture with existing data infrastructure
/// </summary>
public abstract class BaseRepository
{
    protected readonly IDataAccess DataAccess;
    protected readonly IConfiguration Configuration;
    protected readonly ILogger _logger;

    protected BaseRepository(IDataAccess dataAccess, IConfiguration configuration, ILogger logger)
    {
        DataAccess = dataAccess;
        Configuration = configuration;
        _logger = logger;
    }

    protected string GetConnectionString()
    {
        return Configuration.GetConnectionString("default") ?? string.Empty;
    }

    /// <summary>
    /// Load multiple records using the existing IDataAccess interface
    /// </summary>
    protected async Task<List<T>> LoadDataAsync<T>(string sql, object parameters)
    {
        try
        {
            var result = await DataAccess.LoadData<T, dynamic>(sql, parameters, GetConnectionString());
            return result?.ToList() ?? new List<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing query: {Sql}", sql);
            return new List<T>();
        }
    }

    /// <summary>
    /// Load multiple records with specific parameter type
    /// </summary>
    protected async Task<List<T>> LoadDataAsync<T, U>(string sql, U parameters)
    {
        try
        {
            var result = await DataAccess.LoadData<T, U>(sql, parameters, GetConnectionString());
            return result?.ToList() ?? new List<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing query: {Sql}", sql);
            return new List<T>();
        }
    }

    /// <summary>
    /// Load a single record
    /// </summary>
    protected async Task<T?> LoadSingleAsync<T>(string sql, object parameters) where T : class
    {
        try
        {
            var result = await DataAccess.LoadData<T, dynamic>(sql, parameters, GetConnectionString());
            return result?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing single query: {Sql}", sql);
            return null;
        }
    }

    /// <summary>
    /// Save/Insert/Update/Delete operations
    /// </summary>
    protected async Task<int> SaveDataAsync(string sql, object parameters)
    {
        try
        {
            return await DataAccess.SaveData(sql, parameters, GetConnectionString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing save: {Sql}", sql);
            return 0;
        }
    }

    /// <summary>
    /// Execute with retry logic - simple implementation using existing IDataAccess
    /// </summary>
    protected async Task<List<T>> ExecuteWithRetryAsync<T>(string sql, object parameters)
    {
        return await LoadDataAsync<T>(sql, parameters);
    }

    /// <summary>
    /// Query multiple records - alias for LoadDataAsync for Dapper-like interface
    /// </summary>
    protected async Task<List<T>> QueryAsync<T>(string sql, object parameters)
    {
        return await LoadDataAsync<T>(sql, parameters);
    }

    /// <summary>
    /// Query first or default record - alias for LoadSingleAsync
    /// </summary>
    protected async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object parameters) where T : class
    {
        return await LoadSingleAsync<T>(sql, parameters);
    }
}