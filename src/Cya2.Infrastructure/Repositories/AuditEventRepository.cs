using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class AuditEventRepository : IAuditEventRepository
{
    internal const string InsertSql = """
        INSERT INTO AuditEvents (ActorUserId, TargetUserId, OperationId, HostInstanceId, EventType, Outcome, OccurredAtUtc, TraceId, ItemCount)
        VALUES (@ActorUserId, @TargetUserId, @OperationId, @HostInstanceId, @EventType, @Outcome, @OccurredAtUtc, @TraceId, @ItemCount)
        ON DUPLICATE KEY UPDATE Id=Id;
        """;
    private readonly IConfiguration _configuration;
    private readonly IDatabaseGuard _databaseGuard;

    public AuditEventRepository(IConfiguration configuration, IDatabaseGuard databaseGuard)
    {
        _configuration = configuration;
        _databaseGuard = databaseGuard;
    }

    public async Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _databaseGuard.ThrowIfUnavailable();
        await using var connection = new MySqlConnection(_configuration.GetConnectionString("default"));
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            InsertSql,
            auditEvent,
            cancellationToken: cancellationToken));
    }

    public async Task<int> DeleteExpiredAsync(DateTime expiresBeforeUtc, CancellationToken cancellationToken = default)
    {
        _databaseGuard.ThrowIfUnavailable();
        await using var connection = new MySqlConnection(_configuration.GetConnectionString("default"));
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM AuditEvents WHERE OccurredAtUtc < @expiresBeforeUtc;",
            new { expiresBeforeUtc },
            cancellationToken: cancellationToken));
    }

    public async Task<int> FlagInterruptedImportsAsync(string currentHostInstanceId, DateTime startedBeforeUtc, CancellationToken cancellationToken = default)
    {
        _databaseGuard.ThrowIfUnavailable();
        await using var connection = new MySqlConnection(_configuration.GetConnectionString("default"));
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO AuditEvents (ActorUserId, OperationId, HostInstanceId, EventType, Outcome, OccurredAtUtc)
            SELECT s.ActorUserId, s.OperationId, @currentHostInstanceId, 'ImportReconciliationRequired', 'NeedsReconciliation', UTC_TIMESTAMP()
            FROM AuditEvents s
            WHERE s.EventType IN ('DonationImportStarted', 'AccountingImportStarted') AND s.Outcome='Started'
              AND s.HostInstanceId<>@currentHostInstanceId AND s.OccurredAtUtc<@startedBeforeUtc
              AND NOT EXISTS (SELECT 1 FROM AuditEvents t WHERE t.OperationId=s.OperationId AND t.Outcome IN ('Succeeded','Failed','Cancelled','Unknown','Partial','NeedsReconciliation'))
            ON DUPLICATE KEY UPDATE Id=AuditEvents.Id;
            """, new { currentHostInstanceId, startedBeforeUtc }, cancellationToken: cancellationToken));
    }
}
