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

builder.Services.AddCya2HostServices();

builder.Services.AddAuthenticationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();
builder.Services.AddCya2Authentication(builder.Configuration);

builder.Services.AddCya2WebHostServices();

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

app.UseCya2WebPipeline();

app.MapCya2Endpoints();

app.Run();
