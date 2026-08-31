using Cya2.Application.Extensions;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Infrastructure.Extensions;
using Cya2.Infrastructure.Services;
using cya2.Services;
using cya2.Services.Diagnostics;
using cya2.Services.Imports;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection;

namespace cya2.Extensions;

public static class HostServiceCollectionExtensions
{
    public static IServiceCollection AddCya2HostServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<UserSessionHydrationService>();
        services.AddScoped<PageLoadCorrelationContext>();
        services.AddScoped<CircuitHandler, PageLoadCircuitHandler>();
        services.AddHostedService<Cya2.Infrastructure.Services.CacheDataVersionMonitorService>();
        services.AddHttpClient();
        services.AddSingleton<IDatabaseGuard, Cya2.Infrastructure.Services.DatabaseGuardAdapter>();
        services.AddSingleton<ImportProgressService>();
        services.AddSingleton<IImportProgressService>(sp => sp.GetRequiredService<ImportProgressService>());
        services.AddApplicationServices();
        services.AddCleanArchitectureRepositories();
        services.AddScoped<IUserIdResolver, UserIdResolver>();

        return services;
    }
}
