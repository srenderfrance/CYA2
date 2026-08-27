namespace Cya2.Application.Interfaces;

public sealed class DonorNameNormalizationResult
{
    public int DonationDataRowsUpdated { get; set; }
    public int DonationDataBackupRowsUpdated { get; set; }
}

public sealed class DonationRecategorizationResult
{
    public int DonationDataRowsUpdated { get; set; }
}

public interface IDonationImportMaintenanceService
{
    Task<DonorNameNormalizationResult> NormalizeExistingDonorNamesAsync(CancellationToken cancellationToken = default);
    Task<DonationRecategorizationResult> RecategorizeAllDonationsAsync(CancellationToken cancellationToken = default);
}
