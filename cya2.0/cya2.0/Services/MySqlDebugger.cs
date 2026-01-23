using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace cya2.Services
{
    public static class MySqlDebugger
    {
        public static async Task<Dictionary<string, object>> DiagnoseMySqlIssues(
            IConfiguration configuration, 
            ILogger logger)
        {
            var results = new Dictionary<string, object>();
            var connectionString = configuration.GetConnectionString("default") ?? "";
            
            try
            {
                // 1. Test basic connection string parsing
                results["ConnectionStringValid"] = TestConnectionStringParsing(connectionString, logger);
                
                // 2. Test TCP connectivity
                results["TcpConnectivity"] = await TestTcpConnectivity(connectionString, logger);
                
                // 3. Test MySQL connection with minimal timeout
                results["MySqlConnection"] = await TestMySqlConnection(connectionString, logger);
                
                // 4. Test MySQL server status
                results["MySqlServerStatus"] = await TestMySqlServerStatus(connectionString, logger);
                
                // 5. Test connection pool behavior
                results["ConnectionPool"] = await TestConnectionPoolBehavior(connectionString, logger);
                
                // 6. Test large operation simulation
                results["LargeOperationTest"] = await TestLargeOperationTimeout(connectionString, logger);
                
                return results;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MySQL diagnostics failed");
                results["Error"] = ex.Message;
                return results;
            }
        }
        
        private static bool TestConnectionStringParsing(string connectionString, ILogger logger)
        {
            try
            {
                var builder = new MySqlConnectionStringBuilder(connectionString);
                logger.LogInformation("Connection string parsed successfully:");
                logger.LogInformation("  Server: {Server}", builder.Server);
                logger.LogInformation("  Port: {Port}", builder.Port);
                logger.LogInformation("  Database: {Database}", builder.Database);
                logger.LogInformation("  Connection Timeout: {ConnectionTimeout}", builder.ConnectionTimeout);
                logger.LogInformation("  SSL Mode: {SslMode}", builder.SslMode);
                
                // Try to get pool size using indexer
                if (builder.ContainsKey("MaxPoolSize"))
                    logger.LogInformation("  Max Pool Size: {MaxPoolSize}", builder["MaxPoolSize"]);
                
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to parse connection string");
                return false;
            }
        }
        
        private static async Task<Dictionary<string, object>> TestTcpConnectivity(string connectionString, ILogger logger)
        {
            var result = new Dictionary<string, object>();
            
            try
            {
                var builder = new MySqlConnectionStringBuilder(connectionString);
                var host = builder.Server;
                var port = (int)builder.Port;
                
                var tcpResult = GlobalSettings.CheckDatabaseTcpConnection(host, port, 5000);
                result["Success"] = tcpResult;
                result["Host"] = host;
                result["Port"] = port;
                
                logger.LogInformation("TCP connectivity to {Host}:{Port}: {Result}", host, port, tcpResult);
                
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TCP connectivity test failed");
                result["Error"] = ex.Message;
                return result;
            }
        }
        
        private static async Task<Dictionary<string, object>> TestMySqlConnection(string connectionString, ILogger logger)
        {
            var result = new Dictionary<string, object>();
            
            try
            {
                var builder = new MySqlConnectionStringBuilder(connectionString);
                builder.ConnectionTimeout = 30;
                
                using var connection = new MySqlConnection(builder.ConnectionString);
                var startTime = DateTime.UtcNow;
                
                await connection.OpenAsync();
                var connectTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                result["Success"] = true;
                result["ConnectionTimeMs"] = connectTime;
                result["ServerVersion"] = connection.ServerVersion;
                result["Database"] = connection.Database;
                
                logger.LogInformation("MySQL connection successful in {Time}ms, Server: {Version}", 
                    connectTime, connection.ServerVersion);
                
                await connection.CloseAsync();
                
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MySQL connection test failed");
                result["Error"] = ex.Message;
                result["Success"] = false;
                return result;
            }
        }
        
        private static async Task<Dictionary<string, object>> TestMySqlServerStatus(string connectionString, ILogger logger)
        {
            var result = new Dictionary<string, object>();
            
            try
            {
                var builder = new MySqlConnectionStringBuilder(connectionString);
                builder.ConnectionTimeout = 30;
                
                using var connection = new MySqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
                
                // Test server status variables
                var statusQueries = new Dictionary<string, string>
                {
                    ["Threads_connected"] = "SHOW STATUS LIKE 'Threads_connected'",
                    ["Max_connections"] = "SHOW VARIABLES LIKE 'max_connections'",
                    ["Wait_timeout"] = "SHOW VARIABLES LIKE 'wait_timeout'",
                    ["Interactive_timeout"] = "SHOW VARIABLES LIKE 'interactive_timeout'",
                    ["Net_read_timeout"] = "SHOW VARIABLES LIKE 'net_read_timeout'",
                    ["Net_write_timeout"] = "SHOW VARIABLES LIKE 'net_write_timeout'"
                };
                
                foreach (var (key, query) in statusQueries)
                {
                    try
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = query;
                        cmd.CommandTimeout = 10;
                        using var reader = await cmd.ExecuteReaderAsync();
                        if (reader.Read())
                        {
                            result[key] = reader.GetString(1);
                        }
                        reader.Close();
                    }
                    catch (Exception ex)
                    {
                        result[key + "_Error"] = ex.Message;
                    }
                }
                
                logger.LogInformation("MySQL server status retrieved successfully");
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MySQL server status test failed");
                result["Error"] = ex.Message;
                return result;
            }
        }
        
        private static async Task<Dictionary<string, object>> TestConnectionPoolBehavior(string connectionString, ILogger logger)
        {
            var result = new Dictionary<string, object>();
            
            try
            {
                var builder = new MySqlConnectionStringBuilder(connectionString);
                builder["MaxPoolSize"] = 10;
                builder["MinPoolSize"] = 2;
                
                var connections = new List<MySqlConnection>();
                var startTime = DateTime.UtcNow;
                
                // Create multiple connections to test pool
                for (int i = 0; i < 5; i++)
                {
                    var conn = new MySqlConnection(builder.ConnectionString);
                    await conn.OpenAsync();
                    connections.Add(conn);
                }
                
                var poolTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                // Close all connections
                foreach (var conn in connections)
                {
                    await conn.CloseAsync();
                    conn.Dispose();
                }
                
                result["Success"] = true;
                result["PoolCreationTimeMs"] = poolTime;
                result["ConnectionsCreated"] = connections.Count;
                
                logger.LogInformation("Connection pool test successful in {Time}ms", poolTime);
                
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Connection pool test failed");
                result["Error"] = ex.Message;
                return result;
            }
        }
        
        private static async Task<Dictionary<string, object>> TestLargeOperationTimeout(string connectionString, ILogger logger)
        {
            var result = new Dictionary<string, object>();
            
            try
            {
                var builder = new MySqlConnectionStringBuilder(connectionString);
                builder.ConnectionTimeout = 30;
                builder.DefaultCommandTimeout = 60;
                
                using var connection = new MySqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
                
                // Simulate a moderately long operation
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT SLEEP(2)"; // 2 second operation
                cmd.CommandTimeout = 30;
                
                var startTime = DateTime.UtcNow;
                await cmd.ExecuteScalarAsync();
                var executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                result["Success"] = true;
                result["ExecutionTimeMs"] = executionTime;
                
                logger.LogInformation("Large operation test successful in {Time}ms", executionTime);
                
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Large operation test failed");
                result["Error"] = ex.Message;
                return result;
            }
        }
    }
}