using Cya2.Core.Entities;
using Cya2.Core.ReadModels;

namespace Cya2.Application.Models;

public sealed class CacheRebuildData
{
    public IReadOnlyList<Account> Accounts { get; init; } = Array.Empty<Account>();
    public IReadOnlyList<SubAccount> SubAccounts { get; init; } = Array.Empty<SubAccount>();
    public IReadOnlyDictionary<int, List<DonationRecord>> DonationsByAccountId { get; init; } =
        new Dictionary<int, List<DonationRecord>>();
    public IReadOnlyDictionary<string, List<DonationRecord>> DonationsByInternName { get; init; } =
        new Dictionary<string, List<DonationRecord>>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<int, List<AccountingRecord>> AccountingByAccountId { get; init; } =
        new Dictionary<int, List<AccountingRecord>>();
}
