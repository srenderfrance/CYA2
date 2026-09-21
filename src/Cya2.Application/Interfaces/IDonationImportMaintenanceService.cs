namespace Cya2.Application.Interfaces;

public sealed class DonationRecategorizationResult
{
    public int DonationDataRowsUpdated { get; set; }
}

public interface IDonationImportMaintenanceService
{
    Task<DonationRecategorizationResult> RecategorizeAllDonationsAsync(CancellationToken cancellationToken = default);
}
