using cya2;
using cya2.Components;
using cya2.Components.Shared;
using cya2.Middleware;
using cya2.Services;
using cya2.Services.Imports;
using DataLibrary;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Localization;
using OfficeOpenXml;
using Radzen;
using System.Security.Claims;
using System.Threading;

var _lastResetTime = DateTime.Now;
var _lockObject = new object();
DatabaseMonitorService? dbMonitorService = null;

AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
{
    if ((args.Name.Contains("MySql") || args.Name.Contains("mysql", StringComparison.OrdinalIgnoreCase)) &&
        !GlobalSettings.AllowMySqlLoading)
    {
        Console.WriteLine($"Prevented loading of MySQL assembly: {args.Name}");
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
    Console.WriteLine("Added MySQL connection string from environment variable");
}
else
{
    Console.WriteLine("MySQL connection string not found in environment variables");
}

builder.Services.AddLogging(l =>
{
    l.AddConsole();
    l.AddDebug();
});

builder.Services.AddHttpContextAccessor();

// Needed by file upload preview calls and some components/services
builder.Services.AddHttpClient();

builder.Services.AddSingleton<AppState>();
builder.Services.AddScoped<DataLoadingService>();
builder.Services.AddScoped<PageAccountCache>();
builder.Services.AddSingleton<DatabaseMonitorService>();
builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<DatabaseMonitorService>());

// Add SubAccountHelper for handling separate sub accounts
builder.Services.AddScoped<UtilityClasses.SubAccountHelper>();

builder.Services.AddScoped<IDonationImportService, DonationImportService>();
builder.Services.AddScoped<IAccountingImportService, AccountingImportService>();
builder.Services.AddScoped<RollbackService>();
builder.Services.AddSingleton<ImportProgressService>();

builder.Services.AddScoped<IDataAccess>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<SafeDataAccess>>();
    var inner = new DataAccess();
    var monitor = sp.GetRequiredService<DatabaseMonitorService>();
    return new SafeDataAccess(inner, monitor, logger);
});

builder.Services.AddAuthenticationCore();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
builder.Services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp =>
    (ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

builder.Services.AddAuthentication(opts =>
{
    opts.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    opts.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(opts =>
{
    opts.LoginPath = "/api/login";
    opts.AccessDeniedPath = "/not-authorized";
    opts.ExpireTimeSpan = TimeSpan.FromHours(24);
    opts.SlidingExpiration = false;
    opts.Cookie.HttpOnly = true;
    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
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
})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? string.Empty;
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? string.Empty;
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
        OnRemoteFailure = context =>
        {
            context.Response.Redirect("/not-authorized");
            context.HandleResponse();
            return Task.CompletedTask;
        },
        OnTicketReceived = async context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var userRepo = context.HttpContext.RequestServices.GetRequiredService<UserAuth.UserRepository>();
            var monitor = context.HttpContext.RequestServices.GetRequiredService<DatabaseMonitorService>();
            monitor.Suspend();
            try
            {
                var principal = context.Principal;
                if (principal is null)
                {
                    logger.LogWarning("OAuth ticket received without a principal.");
                    context.Fail("Missing principal");
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    context.Response.Redirect("/not-authorized");
                    context.HandleResponse();
                    monitor.Resume();
                    return;
                }

                var email = principal.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrWhiteSpace(email))
                {
                    logger.LogWarning("OAuth principal missing email claim.");
                    context.Fail("Missing email");
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    context.Response.Redirect("/not-authorized");
                    context.HandleResponse();
                    monitor.Resume();
                    return;
                }

                var user = await userRepo.GetUserByEmailAsync(email);
                if (user == null)
                {
                    logger.LogWarning("User not registered: {Email}", email);
                    context.Fail("User not registered");
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    context.Response.Redirect("/not-authorized");
                    context.HandleResponse();
                    monitor.Resume();
                    return;
                }

                if (principal.Identity is not ClaimsIdentity identity)
                {
                    logger.LogWarning("Principal has no ClaimsIdentity.");
                    context.Fail("Invalid identity");
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    context.Response.Redirect("/not-authorized");
                    context.HandleResponse();
                    monitor.Resume();
                    return;
                }

                identity.AddClaim(new Claim(ClaimTypes.Role, user.AuthLevel ?? "User"));
                identity.AddClaim(new Claim("AuthLevel", user.AuthLevel ?? string.Empty));
                identity.AddClaim(new Claim("DefaultAccount", user.DefaultAccount?.ToString() ?? string.Empty));
                identity.AddClaim(new Claim("Language", user.Language ?? string.Empty));
                identity.AddClaim(new Claim("UserId", user.Id.ToString()));
                identity.AddClaim(new Claim("UserName", user.Name ?? string.Empty));

                context.Properties.RedirectUri = user.AuthLevel == "Admin" ? "/admin" : "/";

                monitor.Resume();
            }
            catch (Exception)
            {
                context.Response.Redirect("/error");
                context.HandleResponse();
                monitor.Resume();
            }
        },
        OnCreatingTicket = context =>
        {
            if (context.Properties?.RedirectUri == null)
                context.Properties.RedirectUri = "/";
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("AllowAnonymous", p => p.RequireAssertion(_ => true));
    options.AddPolicy("ErrorPages", p => p.RequireAssertion(_ => true));
    options.AddPolicy("RequireAdmin", p =>
        p.RequireAuthenticatedUser().RequireClaim("AuthLevel", "Admin"));
    options.AddPolicy("CanViewAllAccounts", p =>
        p.RequireAuthenticatedUser().RequireClaim("AuthLevel", new[] { "Admin", "Viewer" }));
});

builder.Services.AddHealthChecks()
    .AddCheck("Database", () =>
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Deferred DB check"));

builder.Services.AddLocalization();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRadzenComponents();

builder.Services.AddControllers();

builder.Services.AddScoped<UserAuth.UserRepository>(sp =>
{
    var da = sp.GetRequiredService<IDataAccess>();
    var cs = builder.Configuration.GetConnectionString("default") ?? string.Empty;
    return new UserAuth.UserRepository(da, cs);
});

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
    logger.LogInformation("Lightweight DB check...");
    var monitor = app.Services.GetRequiredService<DatabaseMonitorService>();
    var config = app.Services.GetRequiredService<IConfiguration>();

    string connectionString = config.GetConnectionString("default") ?? string.Empty;
    string host = "localhost";
    int port = 3306;
    foreach (var part in connectionString.Split(';'))
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

    initialDbConnected = GlobalSettings.CheckDatabaseTcpConnection(host, port, 2000);
}
catch
{
    initialDbConnected = false;
}

GlobalSettings.AllowMySqlLoading = initialDbConnected;
if (!initialDbConnected)
{
    Console.WriteLine("Database unavailable - limited mode");
}

AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    var ex = args.ExceptionObject as Exception;
    Console.Error.WriteLine($"Unhandled exception: {ex?.Message}");
    GlobalSettings.AllowMySqlLoading = false;
    try
    {
        var monitor = app.Services.GetRequiredService<DatabaseMonitorService>();
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

app.MapHealthChecks("/health");

// Initialize monitor service
// Note: this program historically suspends monitoring until ApplicationStarted.
// Keep behavior consistent with the GitHub version.
dbMonitorService = app.Services.GetRequiredService<DatabaseMonitorService>();
//dbMonitorService.Suspend();
// Let the hosted service monitor loop run immediately
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
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
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

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapControllers();

app.MapGet("/api/login", (HttpContext ctx, DatabaseMonitorService monitor, ILogger<Program> logger) =>
{
    logger.LogInformation("Login endpoint invoked");
    foreach (var cookie in ctx.Request.Cookies.Keys)
    {
        if (cookie.Contains("AspNetCore") || cookie.Contains("Microsoft"))
            ctx.Response.Cookies.Delete(cookie);
    }
    ctx.Response.Headers.CacheControl = "no-store, no-cache";
    ctx.Response.Headers.Pragma = "no-cache";

    if (!monitor.IsConnected)
    {
        ctx.Response.Redirect("/database-error", false);
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
    return Results.Challenge(props, new[] { GoogleDefaults.AuthenticationScheme });
});

app.MapGet("/logout", async (HttpContext ctx, AppState state) =>
{
    state.ClearUserData();
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    ctx.Response.Redirect("/logged-out");
});

app.MapGet("/api/check-db", async (HttpContext ctx, IDataAccess da, IConfiguration cfg, ILogger<Program> logger, DatabaseMonitorService monitor) =>
{
    monitor.Suspend();
    try
    {
        var cs = cfg.GetConnectionString("default") ?? string.Empty;
        var ok = await da.CheckConnection(cs);
        monitor.Resume();
        return Results.Ok(ok ? new { status = "connected" } :
            new { status = "disconnected", message = da.LastError });
    }
    catch (Exception ex)
    {
        monitor.Resume();
        return Results.Ok(new { status = "error", message = ex.Message });
    }
});

app.MapGet("/api/auth-status", (HttpContext ctx) =>
{
    var isAuth = ctx.User.Identity?.IsAuthenticated ?? false;
    var claims = ctx.User.Claims.Select(c => new { type = c.Type, value = c.Value }).ToList();
    return Results.Ok(new
    {
        isAuthenticated = isAuth,
        userName = ctx.User.Identity?.Name,
        claims,
        systemStatus = new
        {
            GlobalSettings.CompleteBypass,
            GlobalSettings.AllowMySqlLoading,
            GlobalSettings.BypassDatabaseMonitoring,
            databaseIsConnected = ctx.RequestServices.GetRequiredService<DatabaseMonitorService>().IsConnected,
            isAzureEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")),
            requestPath = ctx.Request.Path.ToString()
        }
    });
}).WithMetadata(new AllowAnonymousAttribute());

// Upload endpoints
app.MapPost("/api/upload/donations", async (HttpRequest req, IDonationImportService import, ILogger<Program> logger) =>
{
    try
    {
        var form = await req.ReadFormAsync();
        var file = form.Files.Count > 0 ? form.Files[0] : null;
        if (file is null || file.Length == 0) return Results.BadRequest("No file");
        await using var stream = file.OpenReadStream();
        var res = await import.StartImportAsync(stream, CancellationToken.None);
        return Results.Json(res);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Donation upload endpoint failed");
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/upload/accounting", async (HttpRequest req, IAccountingImportService import, ILogger<Program> logger) =>
{
    try
    {
        var form = await req.ReadFormAsync();
        var file = form.Files.Count > 0 ? form.Files[0] : null;
        if (file is null || file.Length == 0) return Results.BadRequest("No file");
        await using var stream = file.OpenReadStream();
        var res = await import.StartImportAsync(stream, CancellationToken.None);
        return Results.Json(res);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Accounting upload endpoint failed");
        return Results.Problem(ex.Message);
    }
});

// Rollback endpoints from fixed version
app.MapPost("/api/rollback", async (HttpRequest req, RollbackService rollbackService, ILogger<Program> logger) =>
{
    try
    {
        var body = await req.ReadFromJsonAsync<RollbackRequest>();
        if (body == null || string.IsNullOrEmpty(body.Target))
        {
            return Results.BadRequest("Invalid rollback request");
        }

        logger.LogInformation("Rollback requested for target: {Target}", body.Target);
        var result = await rollbackService.ExecuteRollbackAsync(body.Target);

        if (result.Success)
        {
            logger.LogInformation("Rollback completed successfully: {Message}", result.Message);
        }
        else
        {
            logger.LogWarning("Rollback failed: {Error}", result.ErrorMessage);
        }

        return Results.Json(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Rollback endpoint failed");
        return Results.Problem(ex.Message);
    }
}).RequireAuthorization("RequireAdmin");

app.MapGet("/api/rollback/availability", async (RollbackService rollbackService, ILogger<Program> logger) =>
{
    try
    {
        var availability = await rollbackService.GetRollbackAvailabilityAsync();
        return Results.Json(availability);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Rollback availability check failed");
        return Results.Problem(ex.Message);
    }
}).RequireAuthorization("RequireAdmin");

app.MapGet("/api/mysql-debug", async (IConfiguration config, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("Starting MySQL diagnostics...");
        var diagnostics = await cya2.Services.MySqlDebugger.DiagnoseMySqlIssues(config, logger);
        return Results.Json(diagnostics);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "MySQL diagnostics endpoint failed");
        return Results.Problem($"Diagnostics failed: {ex.Message}");
    }
}).WithMetadata(new AllowAnonymousAttribute());

app.Run();

public record RollbackRequest(string Target);
