namespace Cya2.Core.Entities;

public sealed class AuditOperationScope : IDisposable
{
    private static readonly AsyncLocal<AuditOperationScope?> Ambient = new();
    private readonly AuditOperationScope? _previous;
    public static string HostInstanceId { get; } = Guid.NewGuid().ToString("N");
    public static AuditOperationScope? Current => Ambient.Value;
    public string OperationId { get; }
    public int? ActorUserId { get; }
    public int? TargetUserId { get; }

    private AuditOperationScope(int? actorUserId, string? operationId, int? targetUserId)
    {
        if (operationId is not null && !Guid.TryParseExact(operationId, "N", out _))
            throw new ArgumentException("An audit operation ID must be a generated identifier.", nameof(operationId));
        OperationId = operationId ?? Guid.NewGuid().ToString("N");
        ActorUserId = actorUserId;
        TargetUserId = targetUserId;
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    public static AuditOperationScope Begin(int? actorUserId, string? operationId = null, int? targetUserId = null) =>
        new(actorUserId, operationId, targetUserId);

    public static AuditEvent CreateEvent(int? actorUserId, string eventType, string outcome, string? traceId = null, int? itemCount = null, int? targetUserId = null) => new()
    {
        ActorUserId = actorUserId,
        TargetUserId = targetUserId ?? Current?.TargetUserId,
        OperationId = Current?.OperationId ?? Guid.NewGuid().ToString("N"),
        HostInstanceId = HostInstanceId,
        EventType = eventType,
        Outcome = outcome,
        OccurredAtUtc = DateTime.UtcNow,
        TraceId = traceId,
        ItemCount = itemCount
    };

    public void Dispose() => Ambient.Value = _previous;
}
