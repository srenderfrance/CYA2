using MySql.Data.MySqlClient;

namespace cya2.Extensions;

public static class MySqlConnectionStringConfiguration
{
    public static bool TryNormalize(string? connectionString, uint maximumPoolSize, out string normalizedConnectionString)
    {
        normalizedConnectionString = string.Empty;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return true;
        }

        try
        {
            var builder = new MySqlConnectionStringBuilder(connectionString)
            {
                MaximumPoolSize = maximumPoolSize
            };

            normalizedConnectionString = builder.ConnectionString;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
