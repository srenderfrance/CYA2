namespace Cya2.Core.Entities;

public sealed class AuditEvent
{
    public long Id { get; set; }
    public int? ActorUserId { get; init; }
    public int? TargetUserId { get; init; }
    public string OperationId { get; init; } = Guid.NewGuid().ToString("N");
    public string HostInstanceId { get; init; } = AuditOperationScope.HostInstanceId;
    public string EventType { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; }
    public string? TraceId { get; init; }
    public int? ItemCount { get; init; }
}
