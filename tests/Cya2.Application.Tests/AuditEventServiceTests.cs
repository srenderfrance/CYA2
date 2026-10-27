using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class AuditEventServiceTests
{
    [Fact]
    public async Task RecordAsync_PersistsOnlyApprovedAuditFields()
    {
        var repository = new RecordingAuditEventRepository();
        var service = new AuditEventService(repository, NullLogger<AuditEventService>.Instance);

        await service.RecordAsync(
            actorUserId: 42,
            eventType: AuditEventTypes.DonorExportCompleted,
            outcome: AuditOutcomes.Succeeded,
            traceId: "trace-1",
            itemCount: 3);

        var auditEvent = Assert.Single(repository.Events);
        Assert.Equal(42, auditEvent.ActorUserId);
        Assert.Equal(AuditEventTypes.DonorExportCompleted, auditEvent.EventType);
        Assert.Equal(AuditOutcomes.Succeeded, auditEvent.Outcome);
        Assert.Equal("trace-1", auditEvent.TraceId);
        Assert.Equal(3, auditEvent.ItemCount);
        Assert.Equal(DateTimeKind.Utc, auditEvent.OccurredAtUtc.Kind);
    }

    [Fact]
    public async Task RecordAsync_DoesNotPropagatePersistenceFailures()
    {
        var service = new AuditEventService(new ThrowingAuditEventRepository(), NullLogger<AuditEventService>.Instance);

        var exception = await Record.ExceptionAsync(() => service.RecordAsync(
            actorUserId: 42,
            eventType: AuditEventTypes.RollbackFailed,
            outcome: AuditOutcomes.Failed));

        Assert.Null(exception);
    }

    private sealed class RecordingAuditEventRepository : IAuditEventRepository
    {
        public List<AuditEvent> Events { get; } = [];

        public Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(DateTime expiresBeforeUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class ThrowingAuditEventRepository : IAuditEventRepository
    {
        public Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException();

        public Task<int> DeleteExpiredAsync(DateTime expiresBeforeUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}
