using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace cya2._0.Middleware
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
            bool isSignInCallback = context.Request.Path.StartsWithSegments("/signin-google");

            await _next(context);

            if (isSignInCallback && context.User?.Identity?.IsAuthenticated == true)
            {
                _logger.LogInformation("Post-auth middleware: User is authenticated on signin-google callback");

                // Determine where to redirect based on user's role
                var authLevel = context.User.FindFirstValue("AuthLevel");
                var redirectPath = authLevel == "Admin" ? "/admin" : "/";

                _logger.LogInformation($"Post-auth middleware: User is {authLevel}, redirecting to {redirectPath}");

                if (!context.Response.HasStarted)
                {
                    _logger.LogInformation($"Post-auth middleware: Setting up redirect to {redirectPath}");

                    var script = $@"
            <script>
                console.log('Authentication successful, redirecting to {redirectPath}...');
                // Use sessionStorage instead of localStorage
                sessionStorage.setItem('authRedirectPath', '{redirectPath}');
                window.location.href = '{redirectPath}';
            </script>";

                    await context.Response.WriteAsync(script);
                }
                else
                {
                    _logger.LogWarning("Post-auth middleware: Cannot redirect, response already started");
                }
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