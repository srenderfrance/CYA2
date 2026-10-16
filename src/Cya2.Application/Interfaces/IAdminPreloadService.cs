using Cya2.Application.DTOs;
using Cya2.Application.Models;
using Cya2.Core.Entities;

namespace Cya2.Application.Interfaces;

public interface IAdminPreloadService
{
    Task<AdminPreloadState> PreloadAsync(string userId, CancellationToken cancellationToken = default);
    Task<AdminPreloadState> PreloadMetadataAsync(CancellationToken cancellationToken = default) =>
        PreloadAsync(string.Empty, cancellationToken);
    Task<IReadOnlyList<AdminAccountOverviewDto>> RebuildOverviewFromDataAsync(
        CacheRebuildData data,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AdminAccountOverviewDto>>(Array.Empty<AdminAccountOverviewDto>());
    Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminUserDto>> GetStaffAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminAccountOverviewDto>> GetAccountOverviewAsync(CancellationToken cancellationToken = default);
    void Invalidate();
}

public sealed class AdminPreloadState
{
    public IReadOnlyList<Account> Accounts { get; init; } = Array.Empty<Account>();
    public IReadOnlyList<SubAccount> SubAccounts { get; init; } = Array.Empty<SubAccount>();
    public IReadOnlyList<AdminUserDto> Staff { get; init; } = Array.Empty<AdminUserDto>();
    public IReadOnlyList<AdminAccountOverviewDto> AccountOverview { get; init; } = Array.Empty<AdminAccountOverviewDto>();
}
