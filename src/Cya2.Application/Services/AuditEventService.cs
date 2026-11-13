using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class AuditEventService : IAuditEventService
{
    private readonly IAuditEventRepository _repository;
    private readonly ILogger<AuditEventService> _logger;

    public AuditEventService(IAuditEventRepository repository, ILogger<AuditEventService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task RecordAsync(
        int? actorUserId,
        string eventType,
        string outcome,
        string? traceId = null,
        int? itemCount = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await RecordRequiredAsync(actorUserId, eventType, outcome, traceId, itemCount, cancellationToken);
        }
        catch (AuditPersistenceException)
        {
        }
    }

    public async Task RecordRequiredAsync(int? actorUserId, string eventType, string outcome, string? traceId = null, int? itemCount = null, CancellationToken cancellationToken = default)
    {
        var record = AuditOperationScope.CreateEvent(actorUserId, eventType, outcome, traceId, itemCount);
        if (outcome is AuditOutcomes.Unknown or AuditOutcomes.Partial or AuditOutcomes.NeedsReconciliation)
            _logger.LogWarning(new EventId(9103, "AuditReviewRequired"), "Audit outcome requires review. OperationId={OperationId} EventType={EventType} Outcome={Outcome}", record.OperationId, eventType, outcome);
        try
        {
            await _repository.AddAsync(record, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(new EventId(9100, "AuditWriteFailed"),
                "Audit event persistence failed. OperationId={OperationId} EventType={EventType} Outcome={Outcome} ExceptionType={ExceptionType}",
                record.OperationId, eventType, outcome, ex.GetType().Name);
            throw new AuditPersistenceException();
        }
    }
}
