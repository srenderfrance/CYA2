using System.Threading;
using System.Threading.Tasks;

namespace Cya2.Application.Interfaces
{
    public interface IRollbackService
    {
        Task<RollbackResult> ExecuteRollbackAsync(string target, CancellationToken cancellationToken = default);
        Task<RollbackAvailabilityInfo> GetRollbackAvailabilityAsync();
    }

    public class RollbackResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public int DonationRowsRestored { get; set; }
        public int AccountingRowsRestored { get; set; }
    }

    public class RollbackAvailabilityInfo
    {
        public bool CanRollback { get; set; }
        public bool DonationBackupsAvailable { get; set; }
        public bool AccountingBackupsAvailable { get; set; }
        public BackupInfo? LatestDonationBackup { get; set; }
        public BackupInfo? LatestAccountingBackup { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class BackupInfo
    {
        public string BackupId { get; set; } = string.Empty;
        public DateTime BackupAt { get; set; }
        public int RecordCount { get; set; }
    }
}
