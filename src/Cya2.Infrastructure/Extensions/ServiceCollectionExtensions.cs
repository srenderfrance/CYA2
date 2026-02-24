using Cya2.Core.Interfaces;
using Cya2.Infrastructure.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Cya2.Infrastructure.Extensions;

/// <summary>
/// Service registration for clean architecture infrastructure
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCleanArchitectureRepositories(this IServiceCollection services)
    {
        // Register the repositories that we have implemented
        services.AddScoped<IDonorRepository, DonorRepository>();
        services.AddScoped<IDonationRepository, DonationRepository>();
        
        // TODO: Implement these as needed
        // services.AddScoped<IAccountRepository, AccountRepository>();
        // services.AddScoped<IUserRepository, UserRepository>();

        return services;
    }
}}