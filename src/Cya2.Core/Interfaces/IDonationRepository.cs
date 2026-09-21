using Cya2.Core.DTOs;
using Cya2.Core.ReadModels;

namespace Cya2.Core.Interfaces;

public interface IDonationRepository
{
    Task BackupAndDeleteFromDateAsync(DateTime fromDate, string progressId, CancellationToken cancellationToken);
    Task<DonationImportPersistenceResult> InsertCanonicalDonationsAsync(
        IReadOnlyList<CanonicalDonationWriteModel> donations,
        CancellationToken cancellationToken);

    Task<List<DonationRecord>> GetRecentDonationsForDonorsAsync(
        IEnumerable<(string IdentityKey, string Fund)> donorKeys,
        DateTime beforeDate,
        int maxPerDonor,
        CancellationToken cancellationToken);

    Task<int> RecategorizeAllDonationsAsync(CancellationToken cancellationToken);
}
