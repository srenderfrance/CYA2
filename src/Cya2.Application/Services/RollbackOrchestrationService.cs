using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class RollbackOrchestrationService : IRollbackService
{
    private readonly IRollbackRepository _repository;
    private readonly IRollbackExecutor _executor;
    private readonly IImportCacheInvalidator _cacheInvalidator;
    private readonly IAdminAuthorizationContext _adminAuthorizationContext;
    private readonly IAuditEventService _auditEvents;
    private readonly ILogger<RollbackOrchestrationService> _logger;

    public RollbackOrchestrationService(
        IRollbackRepository repository,
        IRollbackExecutor executor,
        IImportCacheInvalidator cacheInvalidator,
        IAdminAuthorizationContext adminAuthorizationContext,
        IAuditEventService auditEvents,
        ILogger<RollbackOrchestrationService> logger)
    {
        _repository = repository;
        _executor = executor;
        _cacheInvalidator = cacheInvalidator;
        _adminAuthorizationContext = adminAuthorizationContext;
        _auditEvents = auditEvents;
        _logger = logger;
    }

    public async Task<RollbackResult> ExecuteRollbackAsync(string target, CancellationToken cancellationToken = default)
    {
        var actorUserId = await _adminAuthorizationContext.GetCurrentAdminUserIdAsync();
        if (actorUserId is null)
        {
            return new RollbackResult { ErrorMessage = "Administrator authorization is required." };
        }

        RollbackResult result;
        try
        {
            result = target?.ToLowerInvariant() switch
            {
                "donations" => await _executor.RollbackDonationsAsync(cancellationToken),
                "accounting" => await _executor.RollbackAccountingAsync(cancellationToken),
                "both" => await RollbackBothAsync(cancellationToken),
                _ => new RollbackResult { ErrorMessage = "Invalid rollback target." }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Error executing rollback. ExceptionType={ExceptionType}", ex.GetType().Name);
            result = new RollbackResult { ErrorMessage = "Rollback failed." };
        }

        if (result.Success || result.ChangesCommitted || result.OutcomeUnknown)
        {
            try { _cacheInvalidator.InvalidateAll(); }
            catch
            {
                result.Warnings.Add("Data may have changed, but cache refresh failed. Reload and verify the data before continuing.");
                _logger.LogWarning("Rollback cache refresh failed.");
            }
        }
        try
        {
            await _auditEvents.RecordAsync(
                actorUserId,
                result.Success ? AuditEventTypes.RollbackCompleted : AuditEventTypes.RollbackFailed,
                result.Success ? AuditOutcomes.Succeeded : AuditOutcomes.Failed,
                itemCount: result.DonationRowsRestored + result.AccountingRowsRestored,
                cancellationToken: CancellationToken.None);
        }
        catch
        {
            result.Warnings.Add("The rollback result could not be recorded in the audit log.");
            _logger.LogWarning("Rollback terminal audit delivery failed.");
        }

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
            _logger.LogError("Error checking rollback availability. ExceptionType={ExceptionType}", ex.GetType().Name);
            info.ErrorMessage = "Backup availability could not be checked. Try again after connectivity is restored.";
        }

        return info;
    }

    private async Task<RollbackResult> RollbackBothAsync(CancellationToken cancellationToken)
    {
        var donation = await _executor.RollbackDonationsAsync(cancellationToken);
        if (donation.OutcomeUnknown) return donation;
        RollbackResult accounting;
        try { accounting = await _executor.RollbackAccountingAsync(cancellationToken); }
        catch
        {
            accounting = new RollbackResult { ErrorMessage = "Accounting rollback did not complete." };
        }
        var result = new RollbackResult
        {
            Success = donation.Success && accounting.Success,
            ChangesCommitted = donation.Success || donation.ChangesCommitted || accounting.Success || accounting.ChangesCommitted,
            OutcomeUnknown = donation.OutcomeUnknown || accounting.OutcomeUnknown,
            Warnings = [.. donation.Warnings, .. accounting.Warnings],
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
