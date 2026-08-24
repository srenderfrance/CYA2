using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Cya2.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register core working clean architecture services 
        // Financial dashboard service for Home.razor transformation  
        services.AddScoped<IFinancialDashboardService, FinancialDashboardService>();
        services.AddScoped<IAccountCalculationService, AccountCalculationService>();
        services.AddScoped<ISessionAccountDataCacheService, DashboardSessionCacheService>();
        services.AddSingleton<ISessionDashboardDtoCacheService, SessionDashboardDtoCacheService>();
        services.AddScoped<ISessionUserStateService, SessionUserStateService>();
        services.AddSingleton<ISessionImportProgressService, SessionImportProgressService>();
        services.AddScoped<IDateRangeStateService, DateRangeStateService>();
        services.AddScoped<IUserAccountContextService, UserAccountContextService>();
        services.AddSingleton<ICacheInvalidationVersion, CacheInvalidationVersion>();
        services.AddSingleton<IAccountSnapshotCache, AccountSnapshotCache>();
        services.AddScoped<IAccountSnapshotLoader, AccountSnapshotLoader>();

        // Donor analytics service
        services.AddScoped<IDonorAnalyticsService, DonorAnalyticsService>();
        
        // Simple example service to prove clean architecture integration works
        services.AddScoped<ISimpleGreetingService, SimpleGreetingService>();
        
        // Common business services - working implementations
        // These services bridge clean architecture with existing legacy data patterns
        
        // Expense management services - enabled for Expenses.razor migration
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IExpensePresentationService, ExpensePresentationService>();
        services.AddSingleton<ISessionExpenseDataCacheService, SessionExpenseDataCacheService>();

        // Donation management services - enabled for Donations.razor migration
        services.AddScoped<IDonationService, DonationService>();
        services.AddScoped<IDonationPresentationService, DonationPresentationService>();
        services.AddSingleton<ISessionDonationDataCacheService, SessionDonationDataCacheService>();

        // User settings service - enabled for UserSettings.razor migration
        services.AddScoped<IUserSettingsService, UserSettingsService>();
        services.AddScoped<UserManagementService>();
        services.AddScoped<AdminFundReadService>();
        services.AddScoped<AdminFundWriteService>();
        services.AddScoped<IAdminPreloadService, AdminPreloadService>();
        services.AddScoped<IAdminRecentAccountSnapshotService, AdminRecentAccountSnapshotService>();

        // Donor management services - enabled for Donors.razor migration
        services.AddScoped<IDonorService, DonorService>();
        services.AddSingleton<ISessionDonorSummaryCacheService, SessionDonorSummaryCacheService>();

        // Cache invalidation — clears all session caches after import or rollback
        services.AddSingleton<IImportCacheInvalidator, ImportCacheInvalidator>();

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