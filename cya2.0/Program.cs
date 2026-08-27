using cya2;
using cya2.Components;
using cya2.Components.Shared;
using cya2.Middleware;
using cya2.Services;
using cya2.Services.Diagnostics;
using cya2.Services.Imports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using OfficeOpenXml;
using Radzen;
using System.Security.Claims;
using System.Threading;
using System.Threading.RateLimiting;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;
using Cya2.Application.Extensions;
using Cya2.Infrastructure.Extensions;
using Cya2.Infrastructure.Services;

var _lastResetTime = DateTime.Now;
var _lockObject = new object();
IDatabaseAvailabilityMonitor? dbMonitorService = null;

AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
{
    if ((args.Name.Contains("MySql") || args.Name.Contains("mysql", StringComparison.OrdinalIgnoreCase)) &&
        !GlobalSettings.AllowMySqlLoading)
    {
        System.Diagnostics.Debug.WriteLine($"Prevented loading of MySQL assembly: {args.Name}");
        return typeof(object).Assembly;
    }
    return null;
};

var builder = WebApplication.CreateBuilder(args);

ExcelPackage.License.SetNonCommercialOrganization("Servant Partners");

var mysqlConnStr = Environment.GetEnvironmentVariable("MYSQLCONNSTR_default");
if (!string.IsNullOrEmpty(mysqlConnStr))
{
    builder.Configuration["ConnectionStrings:default"] = mysqlConnStr;
}

builder.Services.AddLogging(l =>
{
    l.AddSimpleConsole(options => options.IncludeScopes = true);
    if (builder.Environment.IsDevelopment())
    {
        l.AddDebug();
        l.AddFilter("Microsoft.WebTools.BrowserLink", LogLevel.Error);
        l.AddFilter("Microsoft.AspNetCore.Watch.BrowserRefresh", LogLevel.Error);
        l.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        l.AddFilter("Cya2.Infrastructure.Services.DatabaseMonitorService", LogLevel.Warning);
        l.AddFilter("cya2.Services.CacheDataVersionMonitorService", LogLevel.Warning);
    }
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UserSessionHydrationService>();
builder.Services.AddScoped<PageLoadCorrelationContext>();
builder.Services.AddScoped<CircuitHandler, PageLoadCircuitHandler>();
builder.Services.AddHostedService<CacheDataVersionMonitorService>();

// Needed by file upload preview calls and some components/services
builder.Services.AddHttpClient();

builder.Services.AddSingleton<Cya2.Core.Interfaces.IDatabaseGuard, DatabaseGuardAdapter>();

        builder.Services.AddSingleton<ImportProgressService>();
        builder.Services.AddSingleton<Cya2.Core.Interfaces.IImportProgressService>(sp => sp.GetRequiredService<ImportProgressService>());

        // Clean Architecture Services - Fully enabled
builder.Services.AddApplicationServices();
builder.Services.AddCleanArchitectureRepositories();

// Shared helper: user id resolver (implementation in Infrastructure)
builder.Services.AddScoped<IUserIdResolver, UserIdResolver>();

// Session and cache services are registered in AddApplicationServices().

builder.Services.AddAuthenticationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

// Google OAuth configuration with validation
var googleClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "";
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "";

// Check if we have valid Google OAuth configuration
bool hasValidGoogleConfig = !string.IsNullOrEmpty(googleClientId) && 
                           !string.IsNullOrEmpty(googleClientSecret) &&
                           !googleClientId.Contains("PLACEHOLDER") &&
                           !googleClientSecret.Contains("PLACEHOLDER") &&
                           !googleClientId.Contains("YOUR_GOOGLE_CLIENT_ID") &&
                           !googleClientSecret.Contains("YOUR_GOOGLE_CLIENT_SECRET");

var authBuilder = builder.Services.AddAuthentication(opts =>
{
    opts.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    if (hasValidGoogleConfig)
    {
        opts.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
    }
})
.AddCookie(opts =>
{
    opts.Cookie.Name = "cya2.auth";
    opts.LoginPath = "/api/login";
    opts.AccessDeniedPath = "/not-authorized";
    opts.ExpireTimeSpan = TimeSpan.FromHours(24);
    opts.SlidingExpiration = false;
    opts.Cookie.HttpOnly = true;
    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    opts.Cookie.SameSite = SameSiteMode.Lax;
    opts.Events = new CookieAuthenticationEvents
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
            OnTicketReceived = async context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                var userRepo = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
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

                    var googleId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(googleId))
                    {
                        await FailLoginProcessingAsync("Missing Google subject id");
                        return;
                    }

                    var user = await userRepo.GetByExternalIdAsync(googleId);
                    if (user == null)
                    {
                        var emailMatch = await userRepo.GetByEmailAsync(email);
                        if (emailMatch == null)
                        {
                            await RejectUnauthorizedAsync("User not registered");
                            return;
                        }

                        if (!string.IsNullOrWhiteSpace(emailMatch.GoogleId) && !string.Equals(emailMatch.GoogleId, googleId, StringComparison.Ordinal))
                        {
                            await RejectUnauthorizedAsync("Google ID mismatch");
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(emailMatch.GoogleId))
                        {
                            emailMatch.GoogleId = googleId;
                            await userRepo.UpdateAsync(emailMatch);
                            logger.LogInformation("Bound Google ID to existing user record for {Email}", email);
                        }

                        user = emailMatch;
                    }
                    else if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                    {
                        await RejectUnauthorizedAsync("Email mismatch for Google ID");
                        return;
                    }

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
            },
            OnCreatingTicket = context =>
            {
                context.Properties ??= new AuthenticationProperties();
                if (context.Properties.RedirectUri == null)
                    context.Properties.RedirectUri = "/";
                return Task.CompletedTask;
            }
        };
    });
}

builder.Services.AddAuthorization(options =>
{
    // In development with bypass enabled, make pages more accessible
    var isDevelopment = builder.Environment.IsDevelopment();
    var bypassAuth = builder.Configuration.GetValue<bool>("Development:BypassGoogleAuth", false);
    
    if (isDevelopment && bypassAuth)
    {
        // More permissive policies for development
        options.DefaultPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
            
        // Allow anonymous access to more pages in development
        options.AddPolicy("AllowAnonymous", p => p.RequireAssertion(_ => true));
        options.AddPolicy("ErrorPages", p => p.RequireAssertion(_ => true));
        options.AddPolicy("Development", p => p.RequireAssertion(_ => true));
    }
    else
    {
        // Production policies - require authentication
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    }

    options.AddPolicy("RequireAdmin", p =>
        p.RequireAuthenticatedUser().RequireClaim("AuthLevel", "Admin"));
    options.AddPolicy("CanViewAllAccounts", p =>
        p.RequireAuthenticatedUser().RequireClaim("AuthLevel", new[] { "Admin", "Viewer" }));
    options.AddPolicy("CanAccessExpenses", p =>
        p.RequireAuthenticatedUser().RequireClaim("AuthLevel", new[] { "Admin", "Viewer", "User" }));
});

builder.Services.AddHealthChecks()
    .AddCheck("Database", () =>
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Deferred DB check"));

builder.Services.AddLocalization();

builder.Services.AddRateLimiter(options =>
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

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRadzenComponents();

builder.Services.AddControllers();

// Register user selection service and memory cache
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Cya2.Application.Interfaces.IUserSelectionService, Cya2.Infrastructure.Services.MemoryUserSelectionService>();
builder.Services.AddSingleton<Cya2.Application.Interfaces.IUserDateRangeSelectionService, Cya2.Infrastructure.Services.MemoryUserDateRangeSelectionService>();

var app = builder.Build();

string[] supportedCultures = ["en-US", "es-US"];
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);
app.UseRequestLocalization(localizationOptions);

bool initialDbConnected = false;
try
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogDebug("Lightweight DB check...");
    var monitor = app.Services.GetRequiredService<IDatabaseAvailabilityMonitor>();
    var config = app.Services.GetRequiredService<IConfiguration>();

    string connectionString = config.GetConnectionString("default") ?? string.Empty;
    string host = "localhost";
    int port = 3306;
    foreach (var part in connectionString.Split(";"))
    {
        if (part.StartsWith("server=", StringComparison.OrdinalIgnoreCase) ||
            part.StartsWith("host=", StringComparison.OrdinalIgnoreCase))
        {
            host = part[(part.IndexOf('=') + 1)..].Trim();
        }
        else if (part.StartsWith("port=", StringComparison.OrdinalIgnoreCase))
        {
            int.TryParse(part[(part.IndexOf('=') + 1)..].Trim(), out port);
        }
    }

    logger.LogDebug("Testing database connection to {Host}:{Port}", host, port);
    initialDbConnected = GlobalSettings.CheckDatabaseTcpConnection(host, port, 2000);
    logger.LogDebug("Database connection test result: {Result}", initialDbConnected);
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "Database connectivity check failed");
    initialDbConnected = false;
}

GlobalSettings.AllowMySqlLoading = initialDbConnected;
if (!initialDbConnected)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogDebug("Database unavailable - limited mode");
}

AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    var ex = args.ExceptionObject as Exception;
    Console.Error.WriteLine($"Unhandled exception: {ex?.Message}");
    GlobalSettings.AllowMySqlLoading = false;
    try
    {
        var monitor = app.Services.GetRequiredService<IDatabaseAvailabilityMonitor>();
        monitor.MarkAsDisconnected(ex?.Message ?? "Unhandled exception");
    }
    catch { }
};

TaskScheduler.UnobservedTaskException += (sender, args) =>
{
    args.SetObserved();
    var ex = args.Exception;
    if (ex != null && (ex.ToString().Contains("MySql") || ex.ToString().Contains("Timeout expired")))
    {
        GlobalSettings.AllowMySqlLoading = false;
        try
        {
            dbMonitorService?.Suspend();
        }
        catch { }
    }
};

app.MapHealthChecks("/health").RequireAuthorization();

// Initialize monitor service
dbMonitorService = app.Services.GetRequiredService<IDatabaseAvailabilityMonitor>();
if (!initialDbConnected)
{
    dbMonitorService.MarkAsDisconnected("Startup connectivity check failed");
}
dbMonitorService.Resume();

app.Lifetime.ApplicationStarted.Register(() =>
{
    Task.Delay(5000).ContinueWith(_ =>
    {
        try
        {
            if (initialDbConnected)
            {
                GlobalSettings.AllowMySqlLoading = true;
                dbMonitorService?.Resume();
            }
        }
        catch { }
    });
});

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
    context.Response.Headers["Permissions-Policy"] = "accelerometer=(), autoplay=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=(), clipboard-read=(self), clipboard-write=(self)";
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
// Use middleware to populate session-selected account from server-side selection store
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

app.MapControllers().RequireRateLimiting("ApiPolicy");

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapGet("/api/login", async (HttpContext ctx, IDatabaseAvailabilityMonitor monitor, ILogger<Program> logger) =>
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

    // Check if Google OAuth is configured
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

app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    ctx.Response.Redirect("/logged-out");
}).RequireAuthorization().RequireRateLimiting("AuthPolicy");

app.MapGet("/api/antiforgery-token", (HttpContext ctx, IAntiforgery antiforgery, ILogger<Program> logger) =>
{
    var tokens = antiforgery.GetAndStoreTokens(ctx);
    logger.LogDebug("Issued antiforgery token for {Path}. TraceId={TraceId}, UserAuthenticated={IsAuthenticated}",
        ctx.Request.Path,
        ctx.TraceIdentifier,
        ctx.User?.Identity?.IsAuthenticated == true);
    return Results.Ok(new { requestToken = tokens.RequestToken });
}).RequireAuthorization().RequireRateLimiting("ApiPolicy");

app.Run();
