using Cya2.Core.Entities;

namespace Cya2.Application.Models;

public sealed class AccountMetadataSnapshot
{
    public AccountMetadataSnapshot(Account account, IEnumerable<SubAccount> subAccounts)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        SubAccounts = subAccounts?.ToArray() ?? Array.Empty<SubAccount>();
    }

    public Account Account { get; }
    public IReadOnlyList<SubAccount> SubAccounts { get; }
}
