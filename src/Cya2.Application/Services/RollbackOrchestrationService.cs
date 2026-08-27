using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class RollbackOrchestrationService : IRollbackService
{
    private readonly IRollbackRepository _repository;
    private readonly IRollbackExecutor _executor;
    private readonly IImportCacheInvalidator _cacheInvalidator;
    private readonly ILogger<RollbackOrchestrationService> _logger;

    public RollbackOrchestrationService(
        IRollbackRepository repository,
        IRollbackExecutor executor,
        IImportCacheInvalidator cacheInvalidator,
        ILogger<RollbackOrchestrationService> logger)
    {
        _repository = repository;
        _executor = executor;
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;
    }

    public async Task<RollbackResult> ExecuteRollbackAsync(string target, CancellationToken cancellationToken = default)
    {
        RollbackResult result;
        try
        {
            result = target?.ToLowerInvariant() switch
            {
                "donations" => await _executor.RollbackDonationsAsync(cancellationToken),
                "accounting" => await _executor.RollbackAccountingAsync(cancellationToken),
                "both" => await RollbackBothAsync(cancellationToken),
                _ => new RollbackResult { ErrorMessage = $"Invalid rollback target: {target}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing rollback for target {Target}", target);
            result = new RollbackResult { ErrorMessage = $"Rollback failed: {ex.Message}" };
        }

        if (result.Success)
            _cacheInvalidator.InvalidateAll();

        return result;
    }

    public async Task<RollbackAvailabilityInfo> GetRollbackAvailabilityAsync()
    {
        var info = new RollbackAvailabilityInfo();
        try
        {
            var donationBackups = await _repository.GetAvailableDonationBackupsAsync();
            var accountingBackups = await _repository.GetAvailableAccountingBackupsAsync();
            info.LatestDonationBackup = ToBackupInfo(donationBackups.FirstOrDefault());
            info.LatestAccountingBackup = ToBackupInfo(accountingBackups.FirstOrDefault());
            info.DonationBackupsAvailable = info.LatestDonationBackup is not null;
            info.AccountingBackupsAvailable = info.LatestAccountingBackup is not null;
            info.CanRollback = info.DonationBackupsAvailable || info.AccountingBackupsAvailable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking rollback availability");
            info.ErrorMessage = $"Error checking backup availability: {ex.Message}";
        }

        return info;
    }

    private async Task<RollbackResult> RollbackBothAsync(CancellationToken cancellationToken)
    {
        var donation = await _executor.RollbackDonationsAsync(cancellationToken);
        var accounting = await _executor.RollbackAccountingAsync(cancellationToken);
        var result = new RollbackResult
        {
            Success = donation.Success && accounting.Success,
            Message = $"Donations: {donation.Message}, Accounting: {accounting.Message}",
            DonationRowsRestored = donation.DonationRowsRestored,
            AccountingRowsRestored = accounting.AccountingRowsRestored
        };
        if (!result.Success)
            result.ErrorMessage = $"Errors: {donation.ErrorMessage} {accounting.ErrorMessage}".Trim();
        return result;
    }

    private static BackupInfo? ToBackupInfo(BackupSummary? backup)
        => backup is null
            ? null
            : new BackupInfo
            {
                BackupId = backup.BackupId,
                BackupAt = backup.BackupAt,
                RecordCount = backup.RecordCount,
                MostRecentDataDate = backup.MostRecentDataDate
            };
}
