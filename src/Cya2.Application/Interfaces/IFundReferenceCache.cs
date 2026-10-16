using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IFundReferenceCache
{
    Task<IReadOnlyList<FundReferenceDto>> GetOrCreateAsync(
        long generation,
        Func<CancellationToken, Task<IReadOnlyList<FundReferenceDto>>> factory,
        CancellationToken cancellationToken = default);

    bool TryGet(long generation, out IReadOnlyList<FundReferenceDto> references);
    void InvalidateAll();
}