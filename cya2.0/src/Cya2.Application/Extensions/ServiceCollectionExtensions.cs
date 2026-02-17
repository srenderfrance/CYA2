using Microsoft.Extensions.DependencyInjection;
using Cya2.Application.Services;
using Cya2.Application.Interfaces;

namespace Cya2.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register application services
        services.AddScoped<IDonorService, DonorService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IDonationService, DonationService>();
        
        // Register other application services as they're implemented
        services.AddScoped<DonorAnalyticsService>();
        services.AddScoped<DonationAnalyticsService>();
        services.AddScoped<DataProcessingService>();
        services.AddScoped<AccountCalculationService>();
        services.AddScoped<AccountManagementService>();
        
        // Note: Import services and UserManagementService require external dependencies
        // They will be registered when the infrastructure layer provides implementations
        
        return services;
    }
}