using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Interfaces;
using Cya2.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cya2.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register core working clean architecture services 
        // Financial dashboard service for Home.razor transformation  
        services.AddScoped<IFinancialDashboardService, FinancialDashboardService>();
        services.AddScoped<IAccountCalculationService, AccountCalculationService>();
        services.AddSingleton<IReverseBalanceCalculationService, ReverseBalanceCalculationService>();
        services.AddScoped<IAccountingTransactionProcessor, AccountingTransactionProcessor>();
        services.AddSingleton<ExpenseClassificationService>();
        services.AddSingleton<DonorFrequencyService>();
        services.AddSingleton<DonorMissingGiftService>();
        services.AddScoped<ISessionUserStateService, SessionUserStateService>();
        services.AddScoped<IAccountFactDataService, AccountFactDataService>();
        services.AddSingleton<ISessionImportProgressService, SessionImportProgressService>();
        services.AddScoped<IDateRangeStateService, DateRangeStateService>();
        services.AddScoped<IUserAccountContextService, UserAccountContextService>();
        services.AddScoped<IHomeAuthorizationService, HomeAuthorizationService>();
        services.AddScoped<IUserAuthorizationService, UserAuthorizationService>();
        services.AddScoped<IAuditEventService, AuditEventService>();
        services.AddSingleton<ICacheInvalidationVersion, CacheInvalidationVersion>();
        services.AddSingleton<IDonationDataCache, DonationDataCache>();
        services.AddSingleton<IAccountingDataCache, AccountingDataCache>();
        services.AddSingleton<IUserDonationDataCache, UserDonationDataCache>();
        services.AddSingleton<IUserAccountingDataCache, UserAccountingDataCache>();
        services.AddSingleton<IUserScopedCacheInvalidator, UserScopedCacheInvalidator>();
        services.AddSingleton<IHomeSummaryCache, HomeSummaryCache>();
        services.AddSingleton<IAdminSharedCache, AdminSharedCache>();
        services.AddSingleton<IAccountMetadataCache, AccountMetadataCache>();
        services.AddSingleton<IFundReferenceCache, FundReferenceCache>();
        services.AddScoped<IFundReferenceService, FundReferenceService>();
        services.AddSingleton<IDonorContactCache, DonorContactCache>();
        services.AddScoped<IDonorContactHydrationService, DonorContactHydrationService>();
        services.AddSingleton<IAccountBalanceCache, AccountBalanceCache>();
        services.AddSingleton<IAdminCacheWarmupCoordinator, AdminCacheWarmupCoordinator>();
        services.AddScoped<ICacheRebuildService, CacheRebuildService>();
        services.AddScoped<IDonationDataCacheLoader, DonationDataCacheLoader>();
        services.AddScoped<IAccountingDataCacheLoader, AccountingDataCacheLoader>();
        services.AddScoped<IAccountSelectionCoordinator, AccountSelectionCoordinator>();

        // Common business services - working implementations
        // These services bridge clean architecture with existing legacy data patterns
        
        // Expense management services - enabled for Expenses.razor migration
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IExpensePresentationService, ExpensePresentationService>();

        // Donation management services - enabled for Donations.razor migration
        services.AddScoped<IDonationService, DonationService>();
        services.AddScoped<IDonationPresentationService, DonationPresentationService>();

        // User settings service - enabled for UserSettings.razor migration
        services.AddScoped<IUserSettingsService, UserSettingsService>();
        services.AddScoped<IGoogleSignInService, GoogleSignInService>();
        services.AddScoped<UserManagementService>();
        services.AddScoped<IAccountAccessListingService, AccountAccessListingService>();
        services.AddScoped<AdminFundReadService>();
        services.AddScoped<AdminFundWriteService>();
        services.AddScoped<IAdminPreloadService, AdminPreloadService>();
        services.AddScoped<IAccountSnapshotWarmupService, AccountSnapshotWarmupService>();
        services.AddScoped<IAdminRecentAccountSnapshotService, AdminRecentAccountSnapshotService>();

        // Donor management services - enabled for Donors.razor migration
        services.AddScoped<IDonorService, DonorService>();
        services.AddScoped<IDonorExportService, DonorExportService>();
        services.AddSingleton<IDonorSummaryCache, DonorSummaryCache>();
        services.AddSingleton<IMissingGiftCache, MissingGiftCache>();

        // Cache invalidation — clears all session caches after import or rollback
        services.AddSingleton<IImportCacheInvalidator, ImportCacheInvalidator>();
        services.AddSingleton<ImportPreviewStore>();
        services.AddOptions<ImportUploadOptions>();
        services.AddSingleton<ImportUploadValidator>();
        services.AddScoped<IImportOrchestrationService, ImportOrchestrationService>();
        services.AddScoped<IRollbackService, RollbackOrchestrationService>();

        return services;
    }
}
