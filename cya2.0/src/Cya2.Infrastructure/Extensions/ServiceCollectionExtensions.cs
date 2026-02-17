using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Cya2.Core.Interfaces;
using Cya2.Infrastructure.Data.Repositories;
using Cya2.Infrastructure.Data;
using DataLibrary; // Main application's data access

namespace Cya2.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register enhanced data access that wraps the main application's IDataAccess
        services.AddScoped<EnhancedDataAccess>();

        // Register repository implementations that use the main application's IDataAccess
        // The IDataAccess will be injected from the main application's DI container
        services.AddScoped<IDonorRepository, DonorRepository>();
        services.AddScoped<IDonationRepository, DonationRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        // Register data access configuration
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));

        return services;
    }

    /// <summary>
    /// Register clean architecture repositories to use existing main application's data access
    /// Call this after the main application's services are registered
    /// </summary>
    public static IServiceCollection AddCleanArchitectureRepositories(this IServiceCollection services)
    {
        // These repositories will use the IDataAccess that's already registered in the main application
        services.AddScoped<IDonorRepository, DonorRepository>();
        services.AddScoped<IDonationRepository, DonationRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        return services;
    }
}

public class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeout { get; set; } = 30;
    public bool EnableLogging { get; set; } = false;
}