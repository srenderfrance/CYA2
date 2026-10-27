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
            await _repository.AddAsync(new AuditEvent
            {
                ActorUserId = actorUserId,
                EventType = eventType,
                Outcome = outcome,
                OccurredAtUtc = DateTime.UtcNow,
                TraceId = traceId,
                ItemCount = itemCount
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Audit event persistence failed. EventType={EventType} Outcome={Outcome}",
                eventType,
                outcome);
        }
    }
}
