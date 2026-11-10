using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using cya2.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace cya2.Extensions;

public static class AuthenticationServiceCollectionExtensions
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(4);

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
            options.ExpireTimeSpan = SessionLifetime;
            options.SlidingExpiration = false;
            options.Cookie.IsEssential = true;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Events = new CookieAuthenticationEvents
            {
                OnValidatePrincipal = async context =>
                {
                    var validation = context.HttpContext.RequestServices.GetRequiredService<SessionValidationService>();
                    var result = await validation.ValidateAsync(context.Principal!);
                    if (result is SessionValidationResult.Revoked or SessionValidationResult.Expired)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                },
                OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/_blazor") ||
                        context.Request.Path.StartsWithSegments("/_framework"))
                    {
                        return Task.CompletedTask;
                    }

                    if (context.Request.Path.StartsWithSegments("/api") ||
                        context.Request.Path.StartsWithSegments("/health"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    }
                    else
                    {
                        context.Response.Redirect(context.RedirectUri);
                    }

                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/health"))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
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
                        logger.LogWarning("Google OAuth remote failure. TraceId={TraceId}, ExceptionType={ExceptionType}", context.HttpContext.TraceIdentifier, context.Failure?.GetType().Name ?? "Unknown");
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
                    await RejectUnauthorizedAsync(signInResult.RejectionReason ?? "Unauthorized sign-in attempt");
                    return;
                }

                await RejectUnauthorizedAsync("Unauthorized sign-in attempt");
                return;
            }

            var user = signInResult.User!;
            var authLevel = user.AuthLevel?.Trim() ?? "User";
            context.Properties ??= new AuthenticationProperties();
            var expires = context.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.Add(AuthenticationServiceCollectionExtensions.SessionLifetime);
            context.Properties.ExpiresUtc = expires;
            identity.AddClaim(new Claim(SessionValidationService.ExpiresClaim, expires.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
            identity.AddClaim(new Claim(ClaimTypes.Role, authLevel));
            identity.AddClaim(new Claim("AuthLevel", authLevel));
            identity.AddClaim(new Claim("DefaultAccount", user.DefaultAccount?.ToString() ?? string.Empty));
            var language = string.Equals(user.Language, "es", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(user.Language, "es-US", StringComparison.OrdinalIgnoreCase)
                ? "es-US"
                : "en-US";
            identity.AddClaim(new Claim("Language", language));
            identity.AddClaim(new Claim("UserId", user.Id.ToString()));
            identity.AddClaim(new Claim("UserName", user.Name ?? string.Empty));

            context.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(language)),
                new CookieOptions { Path = "/", IsEssential = true, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps });

            if (string.IsNullOrWhiteSpace(context.Properties.RedirectUri) || context.Properties.RedirectUri == "/")
                context.Properties.RedirectUri = string.Equals(authLevel, "Admin", StringComparison.OrdinalIgnoreCase) ? "/admin" : "/";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Database is currently unavailable", StringComparison.OrdinalIgnoreCase))
        {
            monitor.MarkAsDisconnected("Database unavailable during sign-in");
            logger.LogWarning("Database unavailable during Google ticket processing. TraceId={TraceId}, ExceptionType={ExceptionType}", context.HttpContext.TraceIdentifier, ex.GetType().Name);
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/database-error");
            context.HandleResponse();
        }
        catch (MySql.Data.MySqlClient.MySqlException ex)
        {
            monitor.MarkAsDisconnected("Database unavailable during sign-in");
            logger.LogError("MySQL error during Google ticket processing. TraceId={TraceId}, MySqlErrorCode={ErrorCode}", context.HttpContext.TraceIdentifier, ex.Number);
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/database-error");
            context.HandleResponse();
        }
        catch (Exception ex)
        {
            logger.LogError("Error during Google ticket processing. TraceId={TraceId}, ExceptionType={ExceptionType}", context.HttpContext.TraceIdentifier, ex.GetType().Name);
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/login-error");
            context.HandleResponse();
        }
    }
}
