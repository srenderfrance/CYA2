using Cya2.Core.DTOs;

namespace Cya2.Core.Interfaces;

/// <summary>
/// DB write contract for accounting import operations.
/// Handles backup, delete-range and bulk insert against AccountingData / AccountingDataBackup tables.
/// </summary>
public interface IAccountingImportRepository
{
    /// <summary>Stages validated rows, then atomically snapshots and replaces the overlapping range.</summary>
    Task<ImportBatchResult> ReplaceAsync(IReadOnlyList<AccountingImportRowDto> rows, CancellationToken ct);
}
