using Cya2.Application.Interfaces;
using Cya2.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using System.Threading.RateLimiting;

namespace cya2.Extensions;

public static class WebHostServiceCollectionExtensions
{
    public static IServiceCollection AddCya2WebHostServices(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAuthenticatedUser", policy =>
                policy.RequireAuthenticatedUser());
            options.AddPolicy("AllowAnonymous", policy =>
                policy.RequireAssertion(_ => true));
            options.AddPolicy("ErrorPages", policy =>
                policy.RequireAssertion(_ => true));
            options.AddPolicy("Development", policy =>
                policy.RequireAssertion(_ => true));
            options.AddPolicy("RequireAdmin", policy =>
                policy.RequireAuthenticatedUser().RequireClaim("AuthLevel", "Admin"));
            options.AddPolicy("CanViewAllAccounts", policy =>
                policy.RequireAuthenticatedUser().RequireClaim("AuthLevel", new[] { "Admin", "Viewer" }));
            options.AddPolicy("CanAccessExpenses", policy =>
                policy.RequireAuthenticatedUser().RequireClaim("AuthLevel", new[] { "Admin", "Viewer", "User" }));
        });

        services.AddHealthChecks()
            .AddCheck("Database", () =>
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Deferred DB check"));

        services.AddLocalization();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("AuthPolicy", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("ApiPolicy", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("UploadPolicy", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));
        });

        services.AddRazorComponents()
            .AddInteractiveServerComponents();
        services.AddRadzenComponents();
        services.AddControllers();

        services.AddMemoryCache();
        services.AddSingleton<IUserSelectionService, MemoryUserSelectionService>();
        services.AddSingleton<IUserDateRangeSelectionService, MemoryUserDateRangeSelectionService>();

        return services;
    }
}
