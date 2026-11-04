# Health endpoints

| Route | Access | Healthy | Database unavailable |
| --- | --- | --- | --- |
| `/health/live` | Anonymous | 200 `Healthy` | 200 `Healthy` |
| `/health/ready` | Application authentication cookie | 200 `Healthy` | 503 `Unhealthy` |
| `/health` | Application authentication cookie | Same as readiness | Same as readiness |

Responses contain status-only text, not connection strings, monitor error messages, exception details, account identifiers or health-entry data. Health-check responses disable caching. Anonymous requests to protected routes return 401 without a login/OAuth redirect or dependency check. No administrator role is required, matching the original `/health` authenticated access requirement. HTTPS redirection and existing security headers remain in place; use HTTPS URLs for probes.

## Semantics

Liveness executes no dependency checks. It shows the HTTP application can respond, not that MySQL or financial workflows are usable.

Readiness reads `IDatabaseAvailabilityMonitor.IsConnected`. The existing monitor normally checks TCP connectivity every approximately five seconds, with a connection timeout of up to two seconds. It does not open a SQL connection on every health request and does not expose `LastError`.

This is a monitored connectivity signal, **not** verification of SQL credentials, database schema, permissions, cache warmup or end-to-end application correctness. Status can lag, starts from the monitor's existing initial state, and may remain stale when monitoring is suspended or bypassed. Deployment acceptance must still test authenticated database operations. Startup probing and limited-mode behavior are unchanged.

During an outage, health routes bypass the application's database-error redirect so liveness can return 200 and authenticated readiness can return 503. Normal application pages retain their existing limited-mode redirect. `/health` is retained as a protected compatibility alias but now reports actual monitor state instead of the previous always-healthy placeholder.

## Hosting and access

No platform health-check settings are changed by this implementation. `/health/live` is suitable only when a platform needs an anonymous process-response probe. Do not interpret it as dependency readiness or restart healthy processes merely because a shared database is down.

`/health/ready` and `/health` require the application's normal authentication cookie. They are not anonymous platform readiness probes and no new service credentials or long-lived monitoring tokens are introduced. A platform requiring anonymous dependency-aware readiness needs a separately designed network/access policy before configuring that probe. Do not send user authentication cookies to untrusted monitors.

## Tests

`tests/Cya2.Web.Tests/HealthEndpointTests.cs` exercises the real web pipeline with isolated database-monitor fakes and application cookies. Tests cover live during healthy/unhealthy states without reading the monitor, anonymous readiness rejection without redirects or dependency access, authenticated healthy/outage/recovery responses on both aliases, status-only bodies, cache prevention, and the normal-page limited-mode redirect.

The fake monitor throws if a health endpoint attempts to read diagnostic error details. These tests require neither Docker nor a live MySQL instance. They validate HTTP behavior, not production network connectivity or platform configuration.
