using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace cya2._0
{
    // Shadow implementation to use when database is unavailable
    public class MySqlShadowConnection : IDisposable
    {
        private readonly ILogger<MySqlShadowConnection> _logger;

        public MySqlShadowConnection(string connectionString = null)
        {
            _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<MySqlShadowConnection>();
            _logger.LogWarning("Shadow MySQL connection created - database operations will be blocked");
        }

        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("Blocked attempt to open MySQL connection when database is unavailable");
            return Task.FromException(new InvalidOperationException("Database is unavailable"));
        }

        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}