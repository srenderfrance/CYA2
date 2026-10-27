using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class AuditEventRepository : IAuditEventRepository
{
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
            """
            INSERT INTO AuditEvents (ActorUserId, EventType, Outcome, OccurredAtUtc, TraceId, ItemCount)
            VALUES (@ActorUserId, @EventType, @Outcome, @OccurredAtUtc, @TraceId, @ItemCount);
            """,
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
}
