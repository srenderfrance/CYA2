using cya2.Components;
using Cya2.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.Google;

namespace cya2.Extensions;

public static class WebEndpointExtensions
{
    public static IEndpointRouteBuilder MapCya2Endpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health").RequireAuthorization();
        endpoints.MapControllers().RequireRateLimiting("ApiPolicy");
        endpoints.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        endpoints.MapGet("/api/login", async (HttpContext ctx, IDatabaseAvailabilityMonitor monitor, ILogger<Program> logger) =>
        {
            logger.LogDebug("Login endpoint invoked. TraceId={TraceId}", ctx.TraceIdentifier);

            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            foreach (var cookie in ctx.Request.Cookies.Keys)
            {
                if (cookie.Contains("AspNetCore") || cookie.Contains("Microsoft"))
                {
                    ctx.Response.Cookies.Delete(cookie, new CookieOptions
                    {
                        Path = "/",
                        Secure = true,
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax
                    });
                }
            }

            ctx.Response.Headers.CacheControl = "no-store, no-cache";
            ctx.Response.Headers.Pragma = "no-cache";

            if (!monitor.IsConnected)
            {
                ctx.Response.Redirect("/database-error", false);
                return Results.Empty;
            }

            var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
            var googleClientId = config["Authentication:Google:ClientId"] ?? "";
            var hasValidGoogleConfig = !string.IsNullOrEmpty(googleClientId) &&
                                       !googleClientId.Contains("PLACEHOLDER") &&
                                       !googleClientId.Contains("YOUR_GOOGLE_CLIENT_ID");

            if (!hasValidGoogleConfig)
            {
                logger.LogWarning("Google OAuth not configured. TraceId={TraceId}", ctx.TraceIdentifier);
                ctx.Response.Redirect("/auth-config-required", false);
                return Results.Empty;
            }

            var props = new AuthenticationProperties
            {
                RedirectUri = "/",
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24),
                IsPersistent = true,
                Items = { { "ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() } }
            };

            props.Parameters["prompt"] = "select_account";
            return Results.Challenge(props, new[] { GoogleDefaults.AuthenticationScheme });
        }).RequireRateLimiting("AuthPolicy");

        endpoints.MapGet("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            ctx.Response.Redirect("/logged-out");
        }).RequireAuthorization().RequireRateLimiting("AuthPolicy");

        endpoints.MapGet("/api/antiforgery-token", (HttpContext ctx, IAntiforgery antiforgery, ILogger<Program> logger) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(ctx);
            logger.LogDebug("Issued antiforgery token for {Path}. TraceId={TraceId}, UserAuthenticated={IsAuthenticated}",
                ctx.Request.Path,
                ctx.TraceIdentifier,
                ctx.User?.Identity?.IsAuthenticated == true);
            return Results.Ok(new { requestToken = tokens.RequestToken });
        }).RequireAuthorization().RequireRateLimiting("ApiPolicy");

        return endpoints;
    }
}
