using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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
            // Check if this is the signin-google path
            bool isSignInCallback = context.Request.Path.StartsWithSegments("/signin-google");

            // Call the next middleware in the pipeline
            await _next(context);

            // If this is the signin-google callback URL
            if (isSignInCallback && context.User?.Identity?.IsAuthenticated == true)
            {
                _logger.LogInformation("Post-auth middleware: User is authenticated on signin-google callback");

                // The user has been authenticated - redirect to home using JavaScript
                if (!context.Response.HasStarted)
                {
                    _logger.LogInformation("Post-auth middleware: Setting up JS redirect");

                    // Add a JS script that will redirect
                    var script = @"
                    <script>
                        console.log('Authentication successful, redirecting to home...');
                        window.localStorage.setItem('redirectToHome', 'true');
                        window.location.href = '/';
                    </script>";

                    // Append the script to the response
                    await context.Response.WriteAsync(script);
                }
                else
                {
                    _logger.LogWarning("Post-auth middleware: Cannot set up redirect, response already started");
                }
            }
        }
    }

    public static class PostAuthenticationRedirectMiddlewareExtensions
    {
        public static IApplicationBuilder UsePostAuthenticationRedirect(
            this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PostAuthenticationRedirectMiddleware>();
        }
    }
}