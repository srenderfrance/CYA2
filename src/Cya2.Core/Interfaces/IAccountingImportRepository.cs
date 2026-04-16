using Cya2.Core.DTOs;

namespace Cya2.Core.Interfaces;

/// <summary>
/// DB write contract for accounting import operations.
/// Handles backup, delete-range and bulk insert against AccountingData / AccountingDataBackup tables.
/// </summary>
public interface IAccountingImportRepository
{
    /// <summary>Backs up ALL existing rows and deletes rows with Date >= fromDate.</summary>
    Task BackupAllAndDeleteFromDateAsync(DateTime fromDate, string progressId, CancellationToken ct);

    /// <summary>Bulk-inserts a batch of parsed accounting rows, returns number inserted.</summary>
    Task<ImportBatchResult> BulkInsertAsync(IReadOnlyList<AccountingImportRowDto> batch, CancellationToken ct);
}
