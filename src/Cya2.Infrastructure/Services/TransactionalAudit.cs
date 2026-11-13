using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Infrastructure.Repositories;
using Dapper;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Services;

internal static class TransactionalAudit
{
    public static async Task RecordAsync(MySqlConnection connection, MySqlTransaction transaction, string eventType,
        ILogger logger, int? itemCount = null, int? targetUserId = null, CancellationToken cancellationToken = default)
    {
        var record = AuditOperationScope.CreateEvent(AuditOperationScope.Current?.ActorUserId, eventType, AuditOutcomes.Succeeded,
            itemCount: itemCount, targetUserId: targetUserId);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(AuditEventRepository.InsertSql, record, transaction, cancellationToken: cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogCritical(new EventId(9100, "AuditWriteFailed"),
                "Transactional audit persistence failed. OperationId={OperationId} EventType={EventType} ExceptionType={ExceptionType}",
                record.OperationId, eventType, ex.GetType().Name);
            throw new AuditPersistenceException();
        }
    }
}
