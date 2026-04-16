using Cya2.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class RollbackRepository : IRollbackRepository
{
    private readonly IConfiguration _config;
    private readonly ILogger<RollbackRepository> _logger;
    private readonly IDatabaseGuard _dbGuard;

    public RollbackRepository(IConfiguration config, ILogger<RollbackRepository> logger, IDatabaseGuard dbGuard)
    {
        _config = config;
        _logger = logger;
        _dbGuard = dbGuard;
    }

    private string ConnStr => _config.GetConnectionString("default") ?? string.Empty;

    public async Task<IReadOnlyList<BackupSummary>> GetAvailableDonationBackupsAsync(int limit = 5, CancellationToken ct = default)
    {
        _dbGuard.ThrowIfUnavailable();
        return await QueryBackupsAsync("DonationDataBackup", limit, ct);
    }

    public async Task<IReadOnlyList<BackupSummary>> GetAvailableAccountingBackupsAsync(int limit = 5, CancellationToken ct = default)
    {
        _dbGuard.ThrowIfUnavailable();
        return await QueryBackupsAsync("AccountingDataBackup", limit, ct);
    }

    private async Task<IReadOnlyList<BackupSummary>> QueryBackupsAsync(string tableName, int limit, CancellationToken ct)
    {
        try
        {
            await using var conn = new MySqlConnection(ConnStr);
            await conn.OpenAsync(ct);

            var tableExists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@t",
                new { t = tableName });

            if (tableExists == 0) return Array.Empty<BackupSummary>();

            var sql = $@"
                SELECT CAST(BackupId AS CHAR(36)) AS BackupId, BackupAt, COUNT(*) AS RecordCount
                FROM `{tableName}`
                WHERE Pinned = 0
                GROUP BY BackupId, BackupAt
                ORDER BY BackupAt DESC
                LIMIT @limit";

            var rows = await conn.QueryAsync<BackupSummary>(sql, new { limit });
            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RollbackRepository.QueryBackupsAsync failed for {Table}", tableName);
            return Array.Empty<BackupSummary>();
        }
    }
}
