using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Cya2.Application.Services;

public class UserAccountContextService : IUserAccountContextService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserAccountAccessRepository _userAccountAccessRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
    private readonly ILogger<UserAccountContextService> _logger;
    private static readonly ConcurrentDictionary<string, UserAccountContext> _contextCache = new(StringComparer.OrdinalIgnoreCase);

    public UserAccountContextService(
        IUserRepository userRepository,
        IUserAccountAccessRepository userAccountAccessRepository,
        IAccountRepository accountRepository,
        ICacheInvalidationVersion cacheInvalidationVersion,
        ILogger<UserAccountContextService> logger)
    {
        _userRepository = userRepository;
        _userAccountAccessRepository = userAccountAccessRepository;
        _accountRepository = accountRepository;
        _cacheInvalidationVersion = cacheInvalidationVersion;
        _logger = logger;
    }

    public async Task<UserAccountContext?> GetContextAsync(string userId, bool isAdminOrViewerHint = false)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var normalizedUserId = userId.Trim();
        if (_contextCache.TryGetValue(normalizedUserId, out var cachedContext)
            && cachedContext.CacheVersion == _cacheInvalidationVersion.Current)
        {
            _logger.LogInformation(
                "User account context source=cache user={UserId} isAdminOrViewer={IsAdminOrViewer} defaultAccountId={DefaultAccountId} accounts={AccountCount}",
                normalizedUserId,
                cachedContext.IsAdminOrViewer,
                cachedContext.DefaultAccountId,
                cachedContext.Accounts?.Count ?? 0);
            return CloneContext(cachedContext);
        }

        try
        {
            var user = await ResolveUserAsync(normalizedUserId);
            if (user == null)
            {
                return null;
            }

            var authLevel = user.AuthLevel ?? string.Empty;
            var isAdmin = string.Equals(authLevel, "Admin", StringComparison.OrdinalIgnoreCase);
            var isViewer = string.Equals(authLevel, "Viewer", StringComparison.OrdinalIgnoreCase);

            // Honor trusted caller hint (derived from authenticated claims) to avoid false negatives
            // when DB AuthLevel is stale or inconsistent.
            var canAccessAllAccounts = isAdmin || isViewer || isAdminOrViewerHint;

            if (isAdminOrViewerHint && !(isAdmin || isViewer))
            {
                _logger.LogWarning("Applying admin/viewer hint for user {UserId} with DB AuthLevel '{AuthLevel}'", normalizedUserId, authLevel);
            }

            var context = new UserAccountContext
            {
                UserId = user.Id,
                IsAdminOrViewer = canAccessAllAccounts,
                DefaultAccountId = user.DefaultAccount,
                CacheVersion = _cacheInvalidationVersion.Current,
                Accounts = await GetAccountsAsync(user.Id, canAccessAllAccounts)
            };

            _contextCache[normalizedUserId] = CloneContext(context);
            _logger.LogInformation(
                "User account context source=db user={UserId} isAdminOrViewer={IsAdminOrViewer} defaultAccountId={DefaultAccountId} accounts={AccountCount}",
                normalizedUserId,
                context.IsAdminOrViewer,
                context.DefaultAccountId,
                context.Accounts?.Count ?? 0);

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build user account context for identifier {UserId}", normalizedUserId);
            return null;
        }
    }

    public UserAccountContextAccount? ResolveSelectedAccount(UserAccountContext context, string? preferredFund)
    {
        if (context.Accounts == null || context.Accounts.Count == 0)
        {
            return null;
        }

        var selected = !string.IsNullOrWhiteSpace(preferredFund)
            ? context.Accounts.FirstOrDefault(a => string.Equals(a.Fund, preferredFund, StringComparison.OrdinalIgnoreCase))
            : null;

        selected ??= context.DefaultAccountId.HasValue
            ? context.Accounts.FirstOrDefault(a => a.AccountId == context.DefaultAccountId.Value)
            : null;

        selected ??= context.Accounts.First();
        return selected;
    }

    public void Invalidate(string userId)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            _contextCache.TryRemove(userId.Trim(), out _);
        }
    }

    private async Task<Cya2.Core.Entities.User?> ResolveUserAsync(string userId)
    {
        Cya2.Core.Entities.User? user = null;

        if (int.TryParse(userId, out var parsedUserId))
        {
            user = await _userRepository.GetByIdAsync(parsedUserId);
        }

        if (user == null)
        {
            user = await _userRepository.GetByEmailAsync(userId);
        }

        if (user == null)
        {
            user = await _userRepository.GetByExternalIdAsync(userId);
        }

        return user;
    }

    private async Task<List<UserAccountContextAccount>> GetAccountsAsync(int userId, bool isAdminOrViewer)
    {
        var accounts = isAdminOrViewer
            ? await _accountRepository.GetAllAsync()
            : await _userAccountAccessRepository.GetUserAccountsAsync(userId);

        return accounts.Select(a => new UserAccountContextAccount
        {
            AccountId = a.AccountId,
            Fund = a.Fund,
            AccountingClass = a.AccountingClass,
            AccountNumber = a.AccountNumber,
            CreatedAt = a.CreatedAt,
            Overhead = a.Overhead,
            SoftCredit = a.SoftCredit,
            BalanceAdjustment = a.BalanceAdjustment,
            OtherFunds = a.OtherFunds
        }).ToList();
    }

    private static UserAccountContext CloneContext(UserAccountContext source)
    {
        return new UserAccountContext
        {
            UserId = source.UserId,
            IsAdminOrViewer = source.IsAdminOrViewer,
            DefaultAccountId = source.DefaultAccountId,
            CacheVersion = source.CacheVersion,
            Accounts = (source.Accounts ?? new List<UserAccountContextAccount>())
                .Select(a => new UserAccountContextAccount
                {
                    AccountId = a.AccountId,
                    Fund = a.Fund,
                    AccountingClass = a.AccountingClass,
                    AccountNumber = a.AccountNumber,
                    CreatedAt = a.CreatedAt,
                    Overhead = a.Overhead,
                    SoftCredit = a.SoftCredit,
                    BalanceAdjustment = a.BalanceAdjustment,
                    OtherFunds = a.OtherFunds
                })
                .ToList()
        };
    }
}
