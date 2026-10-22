using cya2;
using cya2.Components;
using cya2.Components.Shared;
using cya2.Extensions;
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
using Cya2.Application.Services;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;
using Cya2.Application.Extensions;
using Cya2.Infrastructure.Extensions;
using Cya2.Infrastructure.Services;
using MySql.Data.MySqlClient;

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

var configuredConnectionString = builder.Configuration.GetConnectionString("default");
var hasInvalidConnectionStringConfiguration =
    !MySqlConnectionStringConfiguration.TryNormalize(configuredConnectionString, 15u, out var normalizedConnectionString);

if (hasInvalidConnectionStringConfiguration)
{
    builder.Configuration["ConnectionStrings:default"] = string.Empty;
}
else if (!string.IsNullOrWhiteSpace(normalizedConnectionString))
{
    builder.Configuration["ConnectionStrings:default"] = normalizedConnectionString;
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

builder.Services.AddCya2HostServices();
builder.Services.Configure<ImportUploadOptions>(
    builder.Configuration.GetSection(ImportUploadOptions.SectionName));
builder.Services.Configure<InitialAdminBootstrapOptions>(
    builder.Configuration.GetSection(InitialAdminBootstrapOptions.SectionName));

builder.Services.AddAuthenticationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();
builder.Services.AddCya2Authentication(builder.Configuration);

builder.Services.AddCya2WebHostServices();

var app = builder.Build();
var lifecycleLogger = app.Services.GetRequiredService<ILogger<Program>>();

if (hasInvalidConnectionStringConfiguration)
{
    lifecycleLogger.LogWarning(
        "The MySQL connection string configuration is invalid. The application will start in database-limited mode.");
}

var effectiveConnectionString = app.Configuration.GetConnectionString("default") ?? string.Empty;
try
{
    var connectionStringBuilder = new MySqlConnectionStringBuilder(effectiveConnectionString);
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    startupLogger.LogInformation(
        "MySQL connection configuration: ProcessId={ProcessId} Machine={Machine} Server={Server} Port={Port} Database={Database} MaximumPoolSize={MaximumPoolSize} MinimumPoolSize={MinimumPoolSize}",
        Environment.ProcessId,
        Environment.MachineName,
        connectionStringBuilder.Server,
        connectionStringBuilder.Port,
        connectionStringBuilder.Database,
        connectionStringBuilder.MaximumPoolSize,
        connectionStringBuilder.MinimumPoolSize);
}
catch (Exception ex)
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    startupLogger.LogWarning(ex, "Unable to parse the effective MySQL connection configuration for diagnostics");
}

string[] supportedCultures = ["en-US", "es-US"];
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);
app.UseRequestLocalization(localizationOptions);

bool initialDbConnected = false;
try
{
    initialDbConnected = DatabaseStartupInitializer.Initialize(app.Services);
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "Database connectivity check failed");
    initialDbConnected = false;
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

// Initialize monitor service
dbMonitorService = app.Services.GetRequiredService<IDatabaseAvailabilityMonitor>();
var cacheWarmupCoordinator = app.Services.GetRequiredService<IAdminCacheWarmupCoordinator>();

dbMonitorService.ConnectionStatusChanged += (_, isConnected) =>
{
    if (!isConnected)
    {
        return;
    }

    GlobalSettings.AllowMySqlLoading = true;
    lifecycleLogger.LogInformation("Database became available; queueing central cache creation");
    cacheWarmupCoordinator.QueueWarmup();
};

app.Lifetime.ApplicationStarted.Register(() =>
{
    lifecycleLogger.LogInformation(
        "ApplicationStarted cache warmup hook registered: initialDbConnected={InitialDbConnected}",
        initialDbConnected);

    _ = Task.Run(async () =>
    {
        await Task.Delay(5000);
        try
        {
            lifecycleLogger.LogInformation(
                "ApplicationStarted cache warmup delay elapsed: initialDbConnected={InitialDbConnected}",
                initialDbConnected);

            if (initialDbConnected)
            {
                GlobalSettings.AllowMySqlLoading = true;
                dbMonitorService?.Resume();
                lifecycleLogger.LogInformation("Starting central cache creation after application startup delay");
                cacheWarmupCoordinator.QueueWarmup();
            }
            else
            {
                app.Services.GetRequiredService<ILogger<Program>>()
                    .LogWarning("Central cache creation skipped because the startup database connectivity check failed");
            }
        }
        catch (Exception ex)
        {
            lifecycleLogger.LogError(ex, "Unable to start central cache creation after application startup delay");
        }
    });
});

app.UseCya2WebPipeline();

app.MapCya2Endpoints();

app.Run();

public partial class Program
{
}
