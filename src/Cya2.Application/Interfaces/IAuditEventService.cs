namespace Cya2.Application.Interfaces;

public static class AuditEventTypes
{
    public const string AdminUserCreated = "AdminUserCreated";
    public const string AdminUserUpdated = "AdminUserUpdated";
    public const string AdminUserDeleted = "AdminUserDeleted";
    public const string AccountAccessGranted = "AccountAccessGranted";
    public const string AccountAccessRevoked = "AccountAccessRevoked";
    public const string DonationImportStarted = "DonationImportStarted";
    public const string DonationImportCompleted = "DonationImportCompleted";
    public const string DonationImportFailed = "DonationImportFailed";
    public const string AccountingImportStarted = "AccountingImportStarted";
    public const string AccountingImportCompleted = "AccountingImportCompleted";
    public const string AccountingImportFailed = "AccountingImportFailed";
    public const string DonorExportCompleted = "DonorExportCompleted";
    public const string DonorExportDenied = "DonorExportDenied";
    public const string RollbackCompleted = "RollbackCompleted";
    public const string RollbackFailed = "RollbackFailed";
    public const string DonationRollbackCompleted = "DonationRollbackCompleted";
    public const string AccountingRollbackCompleted = "AccountingRollbackCompleted";
    public const string ImportReconciliationRequired = "ImportReconciliationRequired";
    public const string DonorExportFailed = "DonorExportFailed";
    public const string AdminUserChangeDenied = "AdminUserChangeDenied";
    public const string AdminUserChangeUnconfirmed = "AdminUserChangeUnconfirmed";
    public const string AdminUserChangeFailed = "AdminUserChangeFailed";
}

public static class AuditOutcomes
{
    public const string Succeeded = "Succeeded";
    public const string Denied = "Denied";
    public const string Failed = "Failed";
    public const string Started = "Started";
    public const string Cancelled = "Cancelled";
    public const string Unknown = "Unknown";
    public const string Partial = "Partial";
    public const string NeedsReconciliation = "NeedsReconciliation";
}

public interface IAuditEventService
{
    Task RecordRequiredAsync(int? actorUserId, string eventType, string outcome, string? traceId = null, int? itemCount = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task RecordAsync(
        int? actorUserId,
        string eventType,
        string outcome,
        string? traceId = null,
        int? itemCount = null,
        CancellationToken cancellationToken = default);
}
