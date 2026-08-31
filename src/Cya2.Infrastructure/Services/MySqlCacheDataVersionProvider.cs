using Cya2.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Services;

public sealed class MySqlCacheDataVersionProvider : ICacheDataVersionProvider
{
    private readonly IConfiguration _configuration;

    public MySqlCacheDataVersionProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<string> GetDataMarkerAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString("default") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var donationMarker = await ReadTableMarkerAsync(connection, "DonationData", cancellationToken);
        var accountingMarker = await ReadTableMarkerAsync(connection, "AccountingData", cancellationToken);

        return $"D:{donationMarker}|A:{accountingMarker}";
    }

    private static async Task<string> ReadTableMarkerAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var commandText = $@"
SELECT
    COUNT(*) AS RowCount,
    COALESCE(MAX(DateCreated), '1900-01-01') AS MaxCreated,
    COALESCE(MAX(`Date`), '1900-01-01') AS MaxDate
FROM {tableName};";

        await using var command = new MySqlCommand(commandText, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return "0|1900-01-01|1900-01-01";
        }

        var rowCount = reader.GetInt64(0);
        var maxCreated = reader.GetDateTime(1);
        var maxDate = reader.GetDateTime(2);

        return $"{rowCount}|{maxCreated:O}|{maxDate:O}";
    }
}
