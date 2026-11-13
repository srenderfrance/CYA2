using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Microsoft.Extensions.Logging;

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

    [Fact]
    public async Task RequiredWriteFailsClosedAndLogsOnlySafeMetadata()
    {
        var logger = new RecordingLogger();
        var service = new AuditEventService(new ThrowingAuditEventRepository(), logger);

        var error = await Assert.ThrowsAsync<AuditPersistenceException>(() => service.RecordRequiredAsync(42, AuditEventTypes.DonationImportStarted, AuditOutcomes.Started));

        Assert.DoesNotContain("private sentinel", error.ToString());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(9100, entry.Id);
        Assert.Equal(LogLevel.Critical, entry.Level);
        Assert.DoesNotContain("private sentinel", entry.Message);
    }

    [Fact]
    public async Task ScopeCorrelatesEventsAndRestoresPreviousIdentity()
    {
        var repository = new RecordingAuditEventRepository();
        var service = new AuditEventService(repository, NullLogger<AuditEventService>.Instance);
        var operationId = Guid.NewGuid().ToString("N");
        using (AuditOperationScope.Begin(42, operationId, 17))
        {
            await service.RecordRequiredAsync(42, AuditEventTypes.DonationImportStarted, AuditOutcomes.Started);
            await Task.Yield();
            await service.RecordAsync(42, AuditEventTypes.DonationImportFailed, AuditOutcomes.Cancelled);
        }

        Assert.Null(AuditOperationScope.Current);
        Assert.All(repository.Events, item => { Assert.Equal(operationId, item.OperationId); Assert.Equal(17, item.TargetUserId); });
    }

    private sealed class RecordingLogger : ILogger<AuditEventService>
    {
        public List<(int Id, LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((eventId.Id, logLevel, formatter(state, exception) + exception));
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
            => throw new InvalidOperationException("private sentinel");

        public Task<int> DeleteExpiredAsync(DateTime expiresBeforeUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}
