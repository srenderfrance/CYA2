using System.Data;
using Dapper;
using MySql.Data.MySqlClient;

namespace DataLibrary
{
    public class DataAccess : IDataAccess
    {
        public async Task<List<T>> LoadData<T, U>(string sql, U parameters, string connectionString)
        {
            try
            {
                using (IDbConnection connection = new MySqlConnection(connectionString))
                {
                    var rows = await connection.QueryAsync<T>(sql, parameters);
                    return rows.ToList();
                }

            }
            catch (Exception ex)
            {
                // Log the exception (use a logging library like Serilog or NLog)
                Console.WriteLine($"Error loading data: {ex.Message}");
                throw; // Re-throw the exception to let the caller handle it
            }
        }
        public async Task<int> SaveData<T>(string sql, T parameters, string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                using (IDbConnection connection = new MySqlConnection(connectionString))
                {
                    var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                    return await connection.ExecuteAsync(command);
                }
            }
    
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error saving data: {ex.Message}");
                throw;
            }
           }

    }
}
