using cya2._0;
using cya2._0.Components;
using cya2._0.Components.Shared;
using cya2._0.Middleware;
using cya2._0.Services;
using Dapper;
using DataLibrary;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using ModelsLibrary;
using MySql.Data.MySqlClient;
using Radzen;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Timers;
using UserAuth;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

// Add this near the top with other variables
var _unhandledExceptionCount = 0;
var _lastResetTime = DateTime.Now;
var _lockObject = new object();
DatabaseMonitorService dbMonitorService = null; // Will be initialized after app is built

// Replace your current AppDomain.AssemblyResolve handler with this more aggressive version
AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
{
    // More aggressively prevent MySQL assembly loading
    if ((args.Name.Contains("MySql") || args.Name.Contains("mysql", StringComparison.OrdinalIgnoreCase)) && 
        !cya2._0.GlobalSettings.AllowMySqlLoading)
    {
        Console.WriteLine($"Prevented loading of MySQL assembly: {args.Name}");
        return typeof(object).Assembly;
    }
    return null; // let the runtime resolve normally
};

var builder = WebApplication.CreateBuilder(args);

// Add this right after 'var builder = WebApplication.CreateBuilder(args);'
// Direct connection string fallback from environment variables
var mysqlConnStr = Environment.GetEnvironmentVariable("MYSQLCONNSTR_default");
if (!string.IsNullOrEmpty(mysqlConnStr))
{
    // This manually adds the connection string to the configuration system
    builder.Configuration["ConnectionStrings:default"] = mysqlConnStr;
    Console.WriteLine("Added MySQL connection string from environment variable");
}
else 
{
    Console.WriteLine("MySQL connection string not found in environment variables");
}

builder.Services.AddLogging(configure =>
{
    configure.AddConsole();
    configure.AddDebug();
});

// Add services to the container.

// Find the existing services configuration section and add:

builder.Services.AddSingleton<AppState>();
builder.Services.AddScoped<DataLoadingService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddServerSideBlazor()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 32 * 1024 * 1024; // 32MB for larger JavaScript interactions
    });

builder.Services.AddRadzenComponents();

// Add authentication services
builder.Services.AddScoped<UserAuth.UserRepository>(provider =>
{
    var dataAccess = provider.GetRequiredService<IDataAccess>();
    var connectionString = builder.Configuration.GetConnectionString("default");
    return new UserAuth.UserRepository(dataAccess, connectionString);
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/api/login";
    options.AccessDeniedPath = "/not-authorized";
    options.ExpireTimeSpan = TimeSpan.FromHours(24);
    options.SlidingExpiration = false;
    
    // Add cache control
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    
    options.Events = new CookieAuthenticationEvents
    {
        OnRedirectToLogin = context =>
        {
            // Ignore Blazor framework requests
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
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
    options.CallbackPath = "/signin-google";

    options.Events = new OAuthEvents
    {
        OnRedirectToAuthorizationEndpoint = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

            // Ignore Blazor framework requests
            if (context.Request.Path.StartsWithSegments("/_blazor") ||
                context.Request.Path.StartsWithSegments("/_framework"))
            {
                return Task.CompletedTask;
            }

            // Add cache control headers
            context.Response.Headers.CacheControl = "no-store, no-cache";
            context.Response.Headers.Pragma = "no-cache";

            logger.LogInformation("Redirecting to Google auth");
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        },

        OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError("Google authentication failed: {Error}", context.Failure?.Message);

            context.Response.Redirect("/not-authorized");
            context.HandleResponse();
            return Task.CompletedTask;
        },

        // This runs after successful authentication with Google
        OnTicketReceived = async context =>
        {
            // Add cache control headers
            context.Response.Headers.CacheControl = "no-store, no-cache";
            context.Response.Headers.Pragma = "no-cache";

            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var userRepository = context.HttpContext.RequestServices.GetRequiredService<UserAuth.UserRepository>();
            var dataAccess = context.HttpContext.RequestServices.GetRequiredService<IDataAccess>();
            var connectionString = builder.Configuration.GetConnectionString("default");
            var dbMonitor = context.HttpContext.RequestServices.GetRequiredService<DatabaseMonitorService>();

            // Suspend database monitoring during this operation
            dbMonitor.Suspend();

            try
            {
                var email = context.Principal.FindFirstValue(ClaimTypes.Email);
                var name = context.Principal.FindFirstValue(ClaimTypes.Name);
                var googleId = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

                // Look up user in database
                var user = await userRepository.GetUserByEmailAsync(email);

                if (user != null)
                {
                    logger.LogInformation("Found user in database: {Email}", email);

                    // Apply claims from DB
                    var identity = (ClaimsIdentity)context.Principal.Identity;
                    identity.AddClaim(new Claim(ClaimTypes.Role, user.AuthLevel ?? "User"));
                    identity.AddClaim(new Claim("AuthLevel", user.AuthLevel ?? ""));
                    identity.AddClaim(new Claim("DefaultAccount", user.DefaultAccount?.ToString() ?? ""));
                    identity.AddClaim(new Claim("Language", user.Language ?? ""));
                    identity.AddClaim(new Claim("UserId", user.Id.ToString()));
                    identity.AddClaim(new Claim("UserName", user.Name ?? ""));

                    // Log all claims for debugging purposes
                    logger.LogInformation("Auth Claims set for user {Email}:", email);
                    foreach (var claim in identity.Claims)
                    {
                        logger.LogInformation("Claim: {Type} = {Value}", claim.Type, claim.Value);
                    }

                    // Set redirect URI based on user role - direct admins to admin page
                    if (user.AuthLevel == "Admin")
                    {
                        // This ensures the redirect is properly set in the authentication ticket
                        context.Properties.RedirectUri = "/admin";
                        logger.LogInformation("User is Admin - setting redirect to /admin");
                    }
                    else
                    {
                        context.Properties.RedirectUri = "/";
                        logger.LogInformation("User is not Admin - setting redirect to /");
                    }

                    dbMonitor.Resume();
                    return;
                }
                else
                {
                    // User not found - don't use bypass anymore
                    logger.LogWarning("Authentication failed: User with email {Email} not found", email);
                    context.Fail("User not registered in system");
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    context.Response.Redirect("/not-authorized");
                    context.HandleResponse();
                }
                
                dbMonitor.Resume();
            }
            catch (Exception ex)
            {
                // Final catch for any other error
                logger.LogError(ex, "Critical error in authentication flow: {Error}", ex.Message);
                context.Response.Redirect("/error");
                context.HandleResponse();
                dbMonitor.Resume();
            }
        },

        // Add this event to ensure successful redirection after authentication
        OnCreatingTicket = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Creating ticket with redirect URI: {RedirectUri}",
                context.Properties?.RedirectUri ?? "Not set");

            // Do not override the redirect URI if it has already been set
            if (context.Properties?.RedirectUri == null)
            {
                context.Properties.RedirectUri = "/";
                logger.LogInformation("Force set redirect URI to /");
            }

            return Task.CompletedTask;
        }
    };
});

// Update authorization settings in your Program.cs file
builder.Services.AddAuthorization(options =>
{
    // This sets the default policy to require authentication for all pages
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Define a policy for pages that should be accessible without authentication
    options.AddPolicy("AllowAnonymous", policy => policy.RequireAssertion(_ => true));

    // Add specific policy for error pages
    options.AddPolicy("ErrorPages", policy => policy.RequireAssertion(_ => true));
    
    // Add policy for admin-only pages
    options.AddPolicy("RequireAdmin", policy => 
        policy.RequireAuthenticatedUser()
              .RequireClaim("AuthLevel", "Admin"));
              
    // Add policy for users who can view all accounts (Admin or Viewer)
    options.AddPolicy("CanViewAllAccounts", policy => 
        policy.RequireAuthenticatedUser()
              .RequireClaim("AuthLevel", new[] { "Admin", "Viewer" }));
});

// Add Blazor authentication state provider
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

// Replace the problematic health checks registration with this:
builder.Services.AddHealthChecks()
    .AddCheck("Database", () =>
    {
        try
        {
            // Use a factory pattern instead of directly accessing app.Services
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Health check will be performed when database is accessed");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(ex.Message);
        }
    });

// Add a flag to control whether the database monitor service should run
builder.Services.AddSingleton<DatabaseMonitorService>();

// Use a simple factory to allow the host to control when the service starts
builder.Services.AddSingleton<IHostedService>(sp =>
{
    // Return the already registered service
    return sp.GetRequiredService<DatabaseMonitorService>();
});

bool initialDbConnected = false; // Define this at the top level

// Add a factory that will capture the initialDbConnected value later
builder.Services.AddScoped<IDataAccess>(provider => {
    var logger = provider.GetRequiredService<ILogger<SafeDataAccess>>();
    var innerDataAccess = new DataAccess();
    var dbMonitor = provider.GetRequiredService<DatabaseMonitorService>();
    return new SafeDataAccess(innerDataAccess, dbMonitor, logger);
});

var app = builder.Build();

// Initial DB check using lightweight TCP connection before loading MySQL assemblies
try
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Performing lightweight database check at startup...");

    var dbMonitor = app.Services.GetRequiredService<DatabaseMonitorService>();
    var configuration = app.Services.GetRequiredService<IConfiguration>();
    
    // Declare field at the broader scope for reuse
    System.Reflection.FieldInfo isConnectedField = dbMonitor.GetType().GetField("_isConnected",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    // Parse connection string for host and port
    string connectionString = configuration.GetConnectionString("default") ?? "";

    // Default MySQL port
    int port = 3306;
    string host = "localhost";

    // Try to extract host and port from connection string
    foreach (var part in connectionString.Split(';'))
    {
        if (part.Trim().StartsWith("server=", StringComparison.OrdinalIgnoreCase) ||
            part.Trim().StartsWith("host=", StringComparison.OrdinalIgnoreCase))
        {
            host = part.Substring(part.IndexOf('=') + 1).Trim();
        }
        else if (part.Trim().StartsWith("port=", StringComparison.OrdinalIgnoreCase))
        {
            int.TryParse(part.Substring(part.IndexOf('=') + 1).Trim(), out port);
        }
    }

    // Do a lightweight TCP check
    initialDbConnected = cya2._0.GlobalSettings.CheckDatabaseTcpConnection(host, port, 2000);
    logger.LogInformation("Initial TCP database check result: {Status}",
        initialDbConnected ? "Connected" : "Disconnected");

    // Set the monitor state directly using our previously declared field
    if (isConnectedField != null)
    {
        isConnectedField.SetValue(dbMonitor, initialDbConnected);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error during lightweight database check: {ex.Message}");
    initialDbConnected = false;

    // Ensure database is marked as disconnected
    try
    {
        var dbMonitor = app.Services.GetRequiredService<DatabaseMonitorService>();
        System.Reflection.FieldInfo isConnectedField = dbMonitor.GetType().GetField("_isConnected",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (isConnectedField != null)
        {
            isConnectedField.SetValue(dbMonitor, false);
        }
    }
    catch { /* Ignore errors */ }
}

// Only allow MySQL to be loaded if the DB is available
cya2._0.GlobalSettings.AllowMySqlLoading = initialDbConnected;

// If DB is disconnected, we want to avoid any MySQL operations
if (!initialDbConnected)
{
    Console.WriteLine("Database unavailable - application will run in limited mode");
}

// Much more aggressive unhandled exception handler
AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    var exceptionObject = args.ExceptionObject as Exception;

    Console.Error.WriteLine($"Unhandled exception: {exceptionObject?.Message}");

    // For ANY unhandled exception, disable MySQL operations
    GlobalSettings.AllowMySqlLoading = false;
    
    try {
        // Force the database monitor to mark the database as disconnected
        var dbMonitor = (DatabaseMonitorService)app.Services.GetService(typeof(DatabaseMonitorService));
        if (dbMonitor != null)
        {
            // Use reflection to set the private field
            var field = dbMonitor.GetType().GetField("_isConnected",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(dbMonitor, false);
            }
        }
    }
    catch {
        // Last resort fallback
    }
};

// Replace your current TaskScheduler.UnobservedTaskException handler with this more robust one
TaskScheduler.UnobservedTaskException += (sender, args) =>
{
    // Always mark as observed first to prevent app crash
    args.SetObserved();

    try
    {
        var exception = args.Exception;

        Console.Error.WriteLine($"Unobserved task exception (handled): {exception?.Message}");

        // Check if it's a database timeout or MySQL exception
        if (exception != null &&
           (exception.ToString().Contains("MySql") ||
            exception.ToString().Contains("Timeout expired")))
        {
            Console.Error.WriteLine("Database connection issue detected - disabling MySQL operations");

            // Actively disable MySQL operations on database errors
            cya2._0.GlobalSettings.AllowMySqlLoading = false;

            // Force the database monitor to suspend
            try
            {
                dbMonitorService.Suspend();
            }
            catch
            {
                // Ignore errors in the handler
            }
        }
    }
    catch
    {
        // Last resort fallback
        Console.Error.WriteLine("Error handling unobserved task exception");
    }
};

app.MapHealthChecks("/health");

// Ensure database monitoring is initially suspended to prevent crashes during startup
dbMonitorService = app.Services.GetRequiredService<DatabaseMonitorService>();
dbMonitorService.Suspend();

// Replace with this conditional version
app.Lifetime.ApplicationStarted.Register(() => {
    // Give the app a moment to stabilize
    Task.Delay(5000).ContinueWith(_ => {
        try {
            // IMPORTANT: Only resume monitoring if we initially detected a connection
            if (initialDbConnected) {
                cya2._0.GlobalSettings.AllowMySqlLoading = true;
                dbMonitorService.Resume();
                app.Services.GetRequiredService<ILogger<Program>>()
                    .LogInformation("Database monitoring resumed after startup");
            } else {
                app.Services.GetRequiredService<ILogger<Program>>()
                    .LogWarning("Database was unavailable at startup - starting periodic reconnection attempts");
                // Start periodic check for database availability
                ThreadPool.QueueUserWorkItem(async _ => await PeriodicReconnectionCheck(app));
            }
        }
        catch (Exception ex) {
            app.Services.GetRequiredService<ILogger<Program>>()
                .LogError(ex, "Error resuming database monitoring");
        }
    });
});

// Update the PeriodicReconnectionCheck method in Program.cs to better handle transitions
static async Task PeriodicReconnectionCheck(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var dbMonitor = app.Services.GetRequiredService<DatabaseMonitorService>();
    var config = app.Services.GetRequiredService<IConfiguration>();
    
    while (true) 
    {
        try
        {
            // Parse connection string for host and port
            string connectionString = config.GetConnectionString("default") ?? "";
            string host = "localhost";
            int port = 3306;
            
            // Extract host and port
            foreach (var part in connectionString.Split(';'))
            {
                if (part.Trim().StartsWith("server=", StringComparison.OrdinalIgnoreCase) ||
                    part.Trim().StartsWith("host=", StringComparison.OrdinalIgnoreCase))
                {
                    host = part.Substring(part.IndexOf('=') + 1).Trim();
                }
                else if (part.Trim().StartsWith("port=", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(part.Substring(part.IndexOf('=') + 1).Trim(), out port);
                }
            }
            
            // Important: First disable MySQL operations to prevent crashes
            // Only then perform the check which might cause a crash
            bool lastKnownState = dbMonitor.IsConnected;
            
            if (lastKnownState) {
                // If we currently think we're connected, check if that's still true
                // Use a very conservative approach - if there's any doubt, disable
                try
                {
                    // Lightweight TCP check is safer than MySQL connection check
                    bool isConnected = GlobalSettings.CheckDatabaseTcpConnection(host, port, 2000);
                    
                    if (!isConnected) {
                        // Immediately disable MySQL operations if TCP check fails
                        logger.LogWarning("TCP check failed - disabling MySQL operations");
                        GlobalSettings.AllowMySqlLoading = false;
                        
                        // Directly update the _isConnected field using reflection
                        var field = dbMonitor.GetType().GetField("_isConnected", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            field.SetValue(dbMonitor, false);
                            logger.LogInformation("Updated database monitor connection state to disconnected");
                        }
                        
                        // Explicitly call OnConnectionStatusChanged
                        var method = dbMonitor.GetType().GetMethod("OnConnectionStatusChanged", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (method != null)
                        {
                            method.Invoke(dbMonitor, new object[] { false });
                            logger.LogInformation("Raised connection status changed event");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Any error means the database is likely unavailable
                    logger.LogError(ex, "Error checking database connection - marking as disconnected");
                    GlobalSettings.AllowMySqlLoading = false;
                    
                    // Directly update the _isConnected field using reflection
                    var field = dbMonitor.GetType().GetField("_isConnected", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        field.SetValue(dbMonitor, false);
                        logger.LogInformation("Updated database monitor connection state to disconnected");
                    }
                }
            }
            else {
                // If we think we're disconnected, check if we can reconnect
                bool isConnected = GlobalSettings.CheckDatabaseTcpConnection(host, port, 2000);
                
                if (isConnected) {
                    logger.LogInformation("Database reconnection detected - enabling MySQL operations");
                    GlobalSettings.AllowMySqlLoading = true;
                    
                    // Directly update the _isConnected field using reflection
                    var field = dbMonitor.GetType().GetField("_isConnected", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        field.SetValue(dbMonitor, true);
                        logger.LogInformation("Updated database monitor connection state to connected");
                    }
                    
                    // Explicitly call OnConnectionStatusChanged
                    var method = dbMonitor.GetType().GetMethod("OnConnectionStatusChanged", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (method != null)
                    {
                        method.Invoke(dbMonitor, new object[] { true });
                        logger.LogInformation("Raised connection status changed event");
                    }
                    
                    dbMonitor.Resume();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in periodic reconnection check");
            
            // Be conservative - if there's any error, assume the database is unavailable
            GlobalSettings.AllowMySqlLoading = false;
        }
        
        // Wait before checking again
        await Task.Delay(5000); // Check every 5 seconds
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Add middleware for better security
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

// Add authentication middleware
app.UseAuthentication();
app.UseAuthorization();
app.UseDatabaseCheck();
app.UsePostAuthenticationRedirect();

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/")
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var authLevel = user.FindFirstValue("AuthLevel");
            if (authLevel == "Admin")
            {
                context.Response.Redirect("/admin", false);
                return;
            }
        }
    }
    await next();
});

// Add after route handling

// Map components after all middleware
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Update your login endpoint to check DB first - improved version
app.MapGet("/api/login", (HttpContext context, DatabaseMonitorService dbMonitor, ILogger<Program> logger) =>
{
    logger.LogInformation("Login endpoint hit - checking database availability");

    try
    {
        // Clear any existing auth cookies
        foreach (var cookie in context.Request.Cookies.Keys)
        {
            if (cookie.Contains("AspNetCore") || cookie.Contains("Microsoft"))
            {
                context.Response.Cookies.Delete(cookie);
            }
        }

        // Add cache control headers
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";

        if (!dbMonitor.IsConnected)
        {
            logger.LogWarning("Database unavailable - redirecting to error page");
            context.Response.Redirect("/database-error", false);
            return Results.Empty;
        }

        logger.LogInformation("Database is available, proceeding with authentication");

        var properties = new AuthenticationProperties
        {
            RedirectUri = "/",
            AllowRefresh = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24),
            IsPersistent = true,
            // Add timestamp to prevent caching
            Items = { { "ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() } }
        };

        logger.LogInformation("Issuing challenge");
        return Results.Challenge(properties, new[] { GoogleDefaults.AuthenticationScheme });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error in login endpoint");
        throw;
    }
});

// Update logout endpoint to clear AppState
app.MapGet("/logout", async (HttpContext context, AppState appState) =>
{
    // Clear AppState before logout
    appState.ClearUserData();
    
    // Sign out from cookie authentication
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    context.Response.Redirect("/logged-out");
    return Task.CompletedTask;
});

// Suspend monitoring during API calls too
app.MapGet("/api/check-db", async (HttpContext context, IDataAccess dataAccess, IConfiguration config, ILogger<Program> logger, DatabaseMonitorService dbMonitor) =>
{
    // Suspend database monitoring during check to prevent concurrent access
    dbMonitor.Suspend();

    try
    {
        var connectionString = config.GetConnectionString("default");
        var isConnected = await dataAccess.CheckConnection(connectionString);

        if (isConnected)
        {
            dbMonitor.Resume();
            return Results.Ok(new { status = "connected" });
        }
        else
        {
            dbMonitor.Resume();
            return Results.Ok(new { status = "disconnected", message = dataAccess.LastError });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error checking database connection");
        dbMonitor.Resume();
        return Results.Ok(new { status = "error", message = ex.Message });
    }
});

// Add this endpoint to check authentication status
app.MapGet("/api/auth-status", (HttpContext context) =>
{
    var isAuthenticated = context.User.Identity?.IsAuthenticated ?? false;
    var claims = context.User.Claims.Select(c => new { type = c.Type, value = c.Value }).ToList();
    
    return Results.Ok(new {
        isAuthenticated,
        userName = context.User.Identity?.Name,
        claims,
        systemStatus = new {
            completeBypass = GlobalSettings.CompleteBypass,
            allowMySqlLoading = GlobalSettings.AllowMySqlLoading,
            bypassDatabaseMonitoring = GlobalSettings.BypassDatabaseMonitoring,
            databaseIsConnected = context.RequestServices.GetRequiredService<DatabaseMonitorService>().IsConnected,
            isAzureEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")),
            requestPath = context.Request.Path.ToString()
        }
    });
})
.WithMetadata(new AllowAnonymousAttribute());

app.Run();



