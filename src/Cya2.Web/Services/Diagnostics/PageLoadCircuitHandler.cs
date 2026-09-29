using Microsoft.AspNetCore.Components.Server.Circuits;

namespace cya2.Services.Diagnostics;

public sealed class PageLoadCircuitHandler : CircuitHandler
{
    private readonly PageLoadCorrelationContext _correlationContext;
    private readonly ILogger<PageLoadCircuitHandler> _logger;

    public PageLoadCircuitHandler(
        PageLoadCorrelationContext correlationContext,
        ILogger<PageLoadCircuitHandler> logger)
    {
        _correlationContext = correlationContext;
        _logger = logger;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _correlationContext.SetCircuitId(circuit.Id);
        // _logger.LogInformation("Blazor circuit opened CircuitId={CircuitId}", circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        // _logger.LogInformation("Blazor circuit closed CircuitId={CircuitId}", circuit.Id);
        return Task.CompletedTask;
    }
}
