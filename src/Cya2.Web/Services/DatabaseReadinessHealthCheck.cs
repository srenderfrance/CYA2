using Cya2.Core.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace cya2.Services;

public sealed class DatabaseReadinessHealthCheck(IDatabaseAvailabilityMonitor monitor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(monitor.IsConnected
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy());
    }
}
