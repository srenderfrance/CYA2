using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IFundReferenceService
{
    Task<IReadOnlyList<FundReferenceDto>> GetAllAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<FundReferenceDto> FilterForUser(
        IReadOnlyList<FundReferenceDto> references,
        int userId,
        bool canAccessAllAccounts);
}