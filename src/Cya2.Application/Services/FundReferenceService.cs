using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;

namespace Cya2.Application.Services;

public sealed class FundReferenceService : IFundReferenceService
{
    private readonly IAccountRepository _accountRepository;
    private readonly IUserAccountAccessRepository _accessRepository;
    private readonly IDonationReadRepository? _donationReadRepository;
    private readonly IFundReferenceCache _cache;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;

    public FundReferenceService(
        IAccountRepository accountRepository,
        IUserAccountAccessRepository accessRepository,
        IDonationReadRepository donationReadRepository,
        IFundReferenceCache cache,
        ICacheInvalidationVersion cacheInvalidationVersion)
    {
        _accountRepository = accountRepository;
        _accessRepository = accessRepository;
        _donationReadRepository = donationReadRepository;
        _cache = cache;
        _cacheInvalidationVersion = cacheInvalidationVersion;
    }

    public FundReferenceService(
        IAccountRepository accountRepository,
        IUserAccountAccessRepository accessRepository,
        IFundReferenceCache cache,
        ICacheInvalidationVersion cacheInvalidationVersion)
        : this(accountRepository, accessRepository, null!, cache, cacheInvalidationVersion)
    {
    }

    public Task<IReadOnlyList<FundReferenceDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var generation = _cacheInvalidationVersion.GetGeneration(
            CacheDataDomains.AccountMetadata | CacheDataDomains.Authorization);

        return _cache.GetOrCreateAsync(generation, LoadAsync, cancellationToken);
    }

    public IReadOnlyList<FundReferenceDto> FilterForUser(
        IReadOnlyList<FundReferenceDto> references,
        int userId,
        bool canAccessAllAccounts) =>
        references.Where(reference => reference.IsAccessibleBy(userId, canAccessAllAccounts)).ToArray();

    private async Task<IReadOnlyList<FundReferenceDto>> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var accountsTask = _accountRepository.GetAllAsync();
        var accessTask = _accessRepository.GetAccountUserIdsAsync();
        await Task.WhenAll(accountsTask, accessTask);
        var accounts = accountsTask.Result;
        return accounts.Select(account => new FundReferenceDto
        {
            AccountId = account.AccountId,
            Fund = account.Fund,
            InternName = string.Empty,
            CreatedAt = account.CreatedAt,
            AccountingClass = account.AccountingClass,
            AccountNumber = account.AccountNumber,
            Overhead = account.Overhead,
            SoftCredit = account.SoftCredit,
            BalanceAdjustment = account.BalanceAdjustment,
            OtherFunds = account.OtherFunds,
            UserIds = new HashSet<int>(accessTask.Result.TryGetValue(account.AccountId, out var userIds)
                ? userIds
                : [])
        }).ToArray();
    }
}