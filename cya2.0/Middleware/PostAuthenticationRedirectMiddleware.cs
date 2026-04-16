using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace cya2.Middleware
{
    public class PostAuthenticationRedirectMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<PostAuthenticationRedirectMiddleware> _logger;

        public PostAuthenticationRedirectMiddleware(RequestDelegate next, ILogger<PostAuthenticationRedirectMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Only handle Google sign-in callbacks
            if (context.Request.Path.StartsWithSegments("/signin-google"))
            {
                _logger.LogInformation("Processing Google sign-in callback");
                
                // Keep these cache control headers
                context.Response.Headers.CacheControl = "no-store, no-cache";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers.Expires = "-1";
                
                await _next(context);

                if (context.User?.Identity?.IsAuthenticated == true && !context.Response.HasStarted)
                {
                    var authLevel = context.User.FindFirstValue("AuthLevel");
                    var redirectPath = authLevel == "Admin" ? "/admin" : "/";

                    // Simplified redirect - no need for JavaScript
                    context.Response.Redirect(redirectPath, false);
                    return;
                }
            }
            else
            {
                await _next(context);
            }
        }
    }

    // Extension method to easily add this middleware to the pipeline
    public static class PostAuthenticationRedirectMiddlewareExtensions
    {
        public static IApplicationBuilder UsePostAuthenticationRedirect(
            this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PostAuthenticationRedirectMiddleware>();
        }
    }
}