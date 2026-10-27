namespace Cya2.Core.Entities;

public sealed class AuditEvent
{
    public long Id { get; set; }
    public int? ActorUserId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; }
    public string? TraceId { get; init; }
    public int? ItemCount { get; init; }
}
