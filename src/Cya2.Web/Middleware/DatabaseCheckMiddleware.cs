using Cya2.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace cya2.Middleware;

public class DatabaseCheckMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DatabaseCheckMiddleware> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDatabaseAvailabilityMonitor _dbMonitor;

    public DatabaseCheckMiddleware(
        RequestDelegate next,
        ILogger<DatabaseCheckMiddleware> logger,
        IConfiguration configuration,
        IDatabaseAvailabilityMonitor dbMonitor)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
        _dbMonitor = dbMonitor;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/css") ||
            context.Request.Path.StartsWithSegments("/js") ||
            context.Request.Path.StartsWithSegments("/lib") ||
            context.Request.Path.StartsWithSegments("/_framework") ||
            context.Request.Path.StartsWithSegments("/_blazor") ||
            context.Request.Path.StartsWithSegments("/favicon.ico") ||
            context.Request.Path.StartsWithSegments("/database-error") ||
            context.Request.Path.StartsWithSegments("/error") ||
            context.Request.Path.StartsWithSegments("/not-authorized") ||
            context.Request.Path.StartsWithSegments("/logged-out"))
        {
            await _next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/signin-google") ||
            context.Request.Path.StartsWithSegments("/api/login"))
        {
            try
            {
                if (!_dbMonitor.IsConnected)
                {
                    _logger.LogWarning("Database unavailable for auth path {Path} - redirecting to error page", context.Request.Path);
                    context.Response.Redirect("/database-error", false);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking database for auth path {Path}", context.Request.Path);
                context.Response.Redirect("/database-error", false);
                return;
            }
        }

        if (!_dbMonitor.IsConnected)
        {
            _logger.LogWarning("Database unavailable - redirecting from {Path} to error page", context.Request.Path);
            context.Response.Redirect("/database-error", false);
            return;
        }

        await _next(context);
    }
}

public static class DatabaseCheckMiddlewareExtensions
{
    public static IApplicationBuilder UseDatabaseCheck(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<DatabaseCheckMiddleware>();
    }
}