using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cya2.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register core working clean architecture services 
        // Financial dashboard service for Home.razor transformation  
        services.AddScoped<IFinancialDashboardService, FinancialDashboardService>();
        
        // Donor analytics service
        services.AddScoped<IDonorAnalyticsService, DonorAnalyticsService>();
        
        // Simple example service to prove clean architecture integration works
        services.AddScoped<ISimpleGreetingService, SimpleGreetingService>();
        
        // Common business services - working implementations
        // These services bridge clean architecture with existing legacy data patterns
        
        // Expense management services - enabled for Expenses.razor migration
        services.AddScoped<IExpenseService, ExpenseService>();
        
        // Donation management services - enabled for Donations.razor migration
        services.AddScoped<IDonationService, DonationService>();

        // Donor management services - enabled for Donors.razor migration
        services.AddScoped<IDonorService, DonorService>();
        
        return services;
    }
}

// Simple service to demonstrate clean architecture works
public interface ISimpleGreetingService
{
    Task<string> GetGreetingAsync(string userName);
    Task<List<string>> GetWelcomeMessagesAsync();
}

public class SimpleGreetingService : ISimpleGreetingService
{
    public Task<string> GetGreetingAsync(string userName)
    {
        return Task.FromResult($"Hello from Clean Architecture, {userName}!");
    }

    public Task<List<string>> GetWelcomeMessagesAsync()
    {
        return Task.FromResult(new List<string>
        {
            "Welcome to your Clean Architecture Dashboard!",
            "Your services are now properly separated.",
            "Business logic is organized and testable."
        });
    }
}