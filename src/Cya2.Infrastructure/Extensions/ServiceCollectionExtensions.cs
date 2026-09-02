using Cya2.Core.Interfaces;
using Cya2.Application.Interfaces;
using Cya2.Infrastructure.Repositories;
using Cya2.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cya2.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCleanArchitectureRepositories(this IServiceCollection services)
    {
        // Database monitor — singleton + hosted service
        services.AddSingleton<DatabaseMonitorService>();
        services.AddSingleton<IDatabaseAvailabilityMonitor>(sp => sp.GetRequiredService<DatabaseMonitorService>());
        services.AddHostedService(sp => sp.GetRequiredService<DatabaseMonitorService>());
        services.AddSingleton<IDatabaseStartupProbe, MySqlDatabaseStartupProbe>();
        services.AddSingleton<ICacheDataVersionProvider, MySqlCacheDataVersionProvider>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserAccountAccessRepository, UserAccountAccessRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ISubAccountRepository, SubAccountRepository>();
        services.AddScoped<IFinancialDashboardReadRepository, FinancialDashboardReadRepository>();
        services.AddScoped<IDonationReadRepository, DonationReadRepository>();
        services.AddScoped<IExpenseReadRepository, ExpenseReadRepository>();
        services.AddScoped<IDonationImportRepository, DonationImportRepository>();
        services.AddScoped<IAccountingImportRepository, AccountingImportRepository>();
        services.AddScoped<IAccountImportService, AccountImportService>();
        services.AddScoped<IRollbackRepository, RollbackRepository>();
        services.AddScoped<IImportProcessor, DonationImportProcessor>();
        services.AddScoped<IImportProcessor, AccountingImportProcessor>();
        services.AddScoped<IDonationImportMaintenanceService, DonationImportProcessor>();
        services.AddScoped<IRollbackExecutor, RollbackExecutor>();

        return services;
    }
}