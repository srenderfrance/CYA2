using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IAccountAccessListingService
{
    Task<IReadOnlyList<AccountAccessUserDto>> GetUsersWithAccessAsync(int accountId, CancellationToken cancellationToken = default);
}
