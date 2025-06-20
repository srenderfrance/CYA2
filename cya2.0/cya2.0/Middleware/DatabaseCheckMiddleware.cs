using cya2._0.Services;
using DataLibrary;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System;
using System.Threading.Tasks;

namespace cya2._0.Middleware
{
    public class DatabaseCheckMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<DatabaseCheckMiddleware> _logger;
        private readonly IConfiguration _configuration;
        private readonly DatabaseMonitorService _dbMonitor; // Use the monitor service

        public DatabaseCheckMiddleware(
            RequestDelegate next,
            ILogger<DatabaseCheckMiddleware> logger,
            IConfiguration configuration,
            DatabaseMonitorService dbMonitor)
        {
            _next = next;
            _logger = logger;
            _configuration = configuration;
            _dbMonitor = dbMonitor;
        }

        public async Task InvokeAsync(HttpContext context, IDataAccess dataAccess)
        {
            // TEMPORARY: Skip all database checks when CompleteBypass is enabled
            // TODO: Remove this condition after Azure testing
            if (GlobalSettings.CompleteBypass)
            {
                await _next(context);
                return;
            }

            // Skip database check for static files and error pages
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

            // Special handling for Google auth callback - this is critical
            if (context.Request.Path.StartsWithSegments("/signin-google"))
            {
                try
                {
                    // Use the monitor's state rather than checking directly
                    if (!_dbMonitor.IsConnected)
                    {
                        _logger.LogWarning("Database unavailable during Google auth callback - redirecting to error page");
                        context.Response.Redirect("/database-error", false);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking database during Google auth callback");
                    context.Response.Redirect("/database-error", false);
                    return;
                }
            }

            // Check API login path
            if (context.Request.Path.StartsWithSegments("/api/login"))
            {
                try
                {
                    // Use the monitor's state rather than checking directly
                    if (!_dbMonitor.IsConnected)
                    {
                        _logger.LogWarning("Database unavailable for login - redirecting to error page");
                        context.Response.Redirect("/database-error", false);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking database for login");
                    context.Response.Redirect("/database-error", false);
                    return;
                }
            }

            // For all other pages, use the cached state from the monitor
            if (!_dbMonitor.IsConnected)
            {
                _logger.LogWarning("Database unavailable - redirecting from {Path} to error page",
                    context.Request.Path);
                context.Response.Redirect("/database-error", false);
                return;
            }

            await _next(context);
        }
    }

    // Extension method
    public static class DatabaseCheckMiddlewareExtensions
    {
        public static IApplicationBuilder UseDatabaseCheck(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<DatabaseCheckMiddleware>();
        }
    }
}