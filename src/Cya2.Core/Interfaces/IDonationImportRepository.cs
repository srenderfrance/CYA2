using Cya2.Core.DTOs;
using Cya2.Core.ReadModels;

namespace Cya2.Core.Interfaces;

/// <summary>
/// DB write contract for donation import operations.
/// Handles backup, delete-range, bulk insert and prior-history queries against DonationData / DonationDataBackup tables.
/// </summary>
public interface IDonationImportRepository
{
    /// <summary>Backs up ALL existing rows and deletes rows with Date >= fromDate.</summary>
    Task BackupAllAndDeleteFromDateAsync(DateTime fromDate, string progressId, CancellationToken ct);

    /// <summary>Bulk-inserts a batch of parsed donation rows, returns number inserted.</summary>
    Task<ImportBatchResult> BulkInsertAsync(IReadOnlyList<DonationImportRowDto> batch, CancellationToken ct);

    /// <summary>
    /// Returns the most recent N donations per donor for all donor/fund combinations provided,
    /// where the donation date is strictly before <paramref name="beforeDate"/>.
    /// Used to supply prior history context for frequency classification during import.
    /// </summary>
    Task<List<DonationRecord>> GetRecentDonationsForDonorsAsync(
        IEnumerable<(string AccountName, string Fund)> donorKeys,
        DateTime beforeDate,
        int maxPerDonor,
        CancellationToken ct);
}
