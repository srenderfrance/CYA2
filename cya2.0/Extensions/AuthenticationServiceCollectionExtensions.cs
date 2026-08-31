using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using cya2.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace cya2.Extensions;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddCya2Authentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var googleClientId = configuration["Authentication:Google:ClientId"] ?? string.Empty;
        var googleClientSecret = configuration["Authentication:Google:ClientSecret"] ?? string.Empty;
        var hasValidGoogleConfig = IsValidGoogleConfiguration(googleClientId, googleClientSecret);

        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            if (hasValidGoogleConfig)
            {
                options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
            }
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = "cya2.auth";
            options.LoginPath = "/api/login";
            options.AccessDeniedPath = "/not-authorized";
            options.ExpireTimeSpan = TimeSpan.FromHours(24);
            options.SlidingExpiration = false;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/_blazor") ||
                        context.Request.Path.StartsWithSegments("/_framework"))
                    {
                        return Task.CompletedTask;
                    }

                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    }
                    else
                    {
                        context.Response.Redirect(context.RedirectUri);
                    }

                    return Task.CompletedTask;
                }
            };
        });

        if (hasValidGoogleConfig)
        {
            authBuilder.AddGoogle(options =>
            {
                options.ClientId = googleClientId;
                options.ClientSecret = googleClientSecret;
                options.CallbackPath = "/signin-google";
                options.Events = new OAuthEvents
                {
                    OnRedirectToAuthorizationEndpoint = context =>
                    {
                        if (context.Request.Path.StartsWithSegments("/_blazor") ||
                            context.Request.Path.StartsWithSegments("/_framework"))
                        {
                            return Task.CompletedTask;
                        }

                        context.Response.Headers.CacheControl = "no-store, no-cache";
                        context.Response.Headers.Pragma = "no-cache";
                        context.Response.Redirect(context.RedirectUri);
                        return Task.CompletedTask;
                    },
                    OnRemoteFailure = async context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogWarning(context.Failure, "Google OAuth remote failure");
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        context.Response.Redirect("/login-error");
                        context.HandleResponse();
                    },
                    OnTicketReceived = GoogleAuthenticationEvents.HandleTicketReceivedAsync
                };
            });
        }

        return services;
    }

    private static bool IsValidGoogleConfiguration(string clientId, string clientSecret) =>
        !string.IsNullOrEmpty(clientId) &&
        !string.IsNullOrEmpty(clientSecret) &&
        !clientId.Contains("PLACEHOLDER") &&
        !clientSecret.Contains("PLACEHOLDER") &&
        !clientId.Contains("YOUR_GOOGLE_CLIENT_ID") &&
        !clientSecret.Contains("YOUR_GOOGLE_CLIENT_SECRET");
}

internal static class GoogleAuthenticationEvents
{
    public static async Task HandleTicketReceivedAsync(TicketReceivedContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        var googleSignInService = context.HttpContext.RequestServices.GetRequiredService<IGoogleSignInService>();
        var monitor = context.HttpContext.RequestServices.GetRequiredService<IDatabaseAvailabilityMonitor>();

        async Task RejectUnauthorizedAsync(string reason)
        {
            logger.LogWarning("Google sign-in rejected. TraceId={TraceId}, Reason={Reason}",
                context.HttpContext.TraceIdentifier,
                reason);
            context.Fail("Unauthorized sign-in attempt");
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/not-authorized");
            context.HandleResponse();
        }

        async Task FailLoginProcessingAsync(string reason)
        {
            logger.LogWarning("Google sign-in processing failed. TraceId={TraceId}, Reason={Reason}",
                context.HttpContext.TraceIdentifier,
                reason);
            context.Fail("Sign-in processing failed");
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/login-error");
            context.HandleResponse();
        }

        try
        {
            if (!monitor.IsConnected)
            {
                await FailLoginProcessingAsync("Database unavailable");
                return;
            }

            var principal = context.Principal;
            if (principal is null)
            {
                await FailLoginProcessingAsync("Missing principal");
                return;
            }

            if (principal.Identity is not ClaimsIdentity identity)
            {
                await FailLoginProcessingAsync("Invalid identity");
                return;
            }

            var email = principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                await FailLoginProcessingAsync("Missing email");
                return;
            }

            var googleId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                           principal.FindFirstValue("sub") ??
                           string.Empty;
            if (string.IsNullOrWhiteSpace(googleId))
            {
                await FailLoginProcessingAsync("Missing Google subject id");
                return;
            }

            var signInResult = await googleSignInService.ResolveUserAsync(googleId, email);
            if (!signInResult.IsAuthorized)
            {
                if (string.Equals(signInResult.RejectionReason, "User not registered", StringComparison.Ordinal) ||
                    string.Equals(signInResult.RejectionReason, "Google ID mismatch", StringComparison.Ordinal))
                {
                    await RejectUnauthorizedAsync(signInResult.RejectionReason);
                    return;
                }

                await RejectUnauthorizedAsync(signInResult.RejectionReason ?? "Unauthorized sign-in attempt");
                return;
            }

            var user = signInResult.User!;
            identity.AddClaim(new Claim(ClaimTypes.Role, user.AuthLevel ?? "User"));
            identity.AddClaim(new Claim("AuthLevel", user.AuthLevel ?? string.Empty));
            identity.AddClaim(new Claim("DefaultAccount", user.DefaultAccount?.ToString() ?? string.Empty));
            identity.AddClaim(new Claim("Language", user.Language ?? string.Empty));
            identity.AddClaim(new Claim("UserId", user.Id.ToString()));
            identity.AddClaim(new Claim("UserName", user.Name ?? string.Empty));

            context.Properties ??= new AuthenticationProperties();
            context.Properties.RedirectUri = user.AuthLevel == "Admin" ? "/admin" : "/";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Database is currently unavailable", StringComparison.OrdinalIgnoreCase))
        {
            monitor.MarkAsDisconnected(ex.Message);
            logger.LogWarning(ex, "Database unavailable during Google ticket processing");
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/database-error");
            context.HandleResponse();
        }
        catch (MySql.Data.MySqlClient.MySqlException ex)
        {
            monitor.MarkAsDisconnected(ex.Message);
            logger.LogError(ex, "MySQL error during Google ticket processing");
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/database-error");
            context.HandleResponse();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during Google ticket processing");
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/login-error");
            context.HandleResponse();
        }
    }
}
