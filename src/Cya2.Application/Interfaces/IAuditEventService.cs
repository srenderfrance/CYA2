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
}

public static class AuditOutcomes
{
    public const string Succeeded = "Succeeded";
    public const string Denied = "Denied";
    public const string Failed = "Failed";
    public const string Started = "Started";
}

public interface IAuditEventService
{
    Task RecordAsync(
        int? actorUserId,
        string eventType,
        string outcome,
        string? traceId = null,
        int? itemCount = null,
        CancellationToken cancellationToken = default);
}
