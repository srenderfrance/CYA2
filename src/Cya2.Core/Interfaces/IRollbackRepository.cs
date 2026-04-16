namespace Cya2.Core.Interfaces;

public sealed class BackupSummary
{
    public string BackupId { get; set; } = string.Empty;
    public DateTime BackupAt { get; set; }
    public int RecordCount { get; set; }
}

/// <summary>
/// Read contract for querying available import backup metadata.
/// The actual rollback (delete + restore) continues to use direct MySqlConnection
/// inside RollbackService because it requires a single long-running transaction.
/// </summary>
public interface IRollbackRepository
{
    Task<IReadOnlyList<BackupSummary>> GetAvailableDonationBackupsAsync(int limit = 5, CancellationToken ct = default);
    Task<IReadOnlyList<BackupSummary>> GetAvailableAccountingBackupsAsync(int limit = 5, CancellationToken ct = default);
}
