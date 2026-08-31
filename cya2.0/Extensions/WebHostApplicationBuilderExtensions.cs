using cya2.Middleware;
using Microsoft.AspNetCore.Builder;
using System.Security.Claims;

namespace cya2.Extensions;

public static class WebHostApplicationBuilderExtensions
{
    public static WebApplication UseCya2WebPipeline(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseAntiforgery();

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["Permissions-Policy"] = "accelerometer=(), autoplay=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), payment=(), usb=(), clipboard-read=(self), clipboard-write=(self)";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "base-uri 'self'; " +
                "object-src 'none'; " +
                "frame-ancestors 'none'; " +
                "form-action 'self'; " +
                "script-src 'self' https://cdn.jsdelivr.net; " +
                "script-src-elem 'self' https://cdn.jsdelivr.net; " +
                "script-src-attr 'unsafe-inline'; " +
                "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
                "font-src 'self' https://cdn.jsdelivr.net data:; " +
                "img-src 'self' data: https:; " +
                "connect-src 'self' https: wss:; " +
                "frame-src 'none';";
            await next();
        });

        app.Use(async (context, next) =>
        {
            if (!app.Environment.IsDevelopment() &&
                context.Request.Path.Equals("/auth-config-required", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next();
        });

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseSelectedAccountMiddleware();
        app.UseDatabaseCheck();

        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/")
            {
                var user = context.User;
                if (user?.Identity?.IsAuthenticated == true &&
                    user.FindFirstValue("AuthLevel") == "Admin")
                {
                    context.Response.Redirect("/admin", false);
                    return;
                }
            }

            await next();
        });

        return app;
    }
}
