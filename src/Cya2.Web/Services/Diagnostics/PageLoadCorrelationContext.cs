using Microsoft.Extensions.Logging;

namespace cya2.Services.Diagnostics;

public sealed class PageLoadCorrelationContext
{
    private readonly string _scopeId = Guid.NewGuid().ToString("N");

    public string? CircuitId { get; private set; }

    public void SetCircuitId(string circuitId)
    {
        CircuitId = circuitId;
    }

    public IDisposable BeginOperation(ILogger logger, string page, string operation)
    {
        // return logger.BeginScope(
        //     "Page={Page} Operation={Operation} OperationId={OperationId} CircuitId={CircuitId} ScopeId={ScopeId}",
        //     page,
        //     operation,
        //     Guid.NewGuid().ToString("N"),
        //     CircuitId ?? "prerender",
        //     _scopeId) ?? NullScope.Instance;
        return NullScope.Instance;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
