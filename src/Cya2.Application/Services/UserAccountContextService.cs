using Cya2.Application.Interfaces;
using Cya2.Application.DTOs;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Cya2.Core.Utilities;

namespace Cya2.Application.Services;

public class UserAccountContextService : IUserAccountContextService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserAccountAccessRepository _userAccountAccessRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IFundReferenceService? _fundReferenceService;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
    private readonly ILogger<UserAccountContextService> _logger;
    private const int MaxContextCacheEntries = 256;
    private static readonly object ContextCacheSync = new();
    private static readonly Dictionary<string, CachedContext> ContextCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> ContextCacheLru = new();

    public UserAccountContextService(
        IUserRepository userRepository,
        IUserAccountAccessRepository userAccountAccessRepository,
        IAccountRepository accountRepository,
        ICacheInvalidationVersion cacheInvalidationVersion,
        ILogger<UserAccountContextService> logger,
        IFundReferenceService? fundReferenceService = null)
    {
        _userRepository = userRepository;
        _userAccountAccessRepository = userAccountAccessRepository;
        _accountRepository = accountRepository;
        _fundReferenceService = fundReferenceService!;
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
        var currentUser = await ResolveUserAsync(normalizedUserId);
        if (currentUser is null)
        {
            Invalidate(normalizedUserId);
            return null;
        }

        var currentCanAccessAllAccounts = currentUser.CanViewAllAccounts();
        try
        {
            var contextGeneration = _cacheInvalidationVersion.GetGeneration(
                CacheDataDomains.AccountMetadata | CacheDataDomains.Authorization);
            if (TryGetCachedContext(normalizedUserId, contextGeneration, currentCanAccessAllAccounts, out var cachedContext))
            {
                return cachedContext;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation("User account context phase=user-start user={UserId}", normalizedUserId);
            var user = currentUser;
            // _logger.LogInformation("User account context phase=user-complete elapsedMs={ElapsedMs} found={Found}", stopwatch.ElapsedMilliseconds, user is not null);
            if (user == null)
            {
                return null;
            }

            var authLevel = user.AuthLevel?.Trim() ?? string.Empty;
            var isAdmin = string.Equals(authLevel, "Admin", StringComparison.OrdinalIgnoreCase);
            var isViewer = string.Equals(authLevel, "Viewer", StringComparison.OrdinalIgnoreCase);

            var canAccessAllAccounts = isAdmin || isViewer;

            var context = new UserAccountContext
            {
                UserId = user.Id,
                IsAdminOrViewer = canAccessAllAccounts,
                IsViewer = isViewer,
                IsIntern = user.IsIntern(),
                InternName = user.IsIntern()
                    ? InternAccountUtility.GetInternNameFromUserName(user.Name)
                    : string.Empty,
                DefaultAccountId = user.DefaultAccount,
                CacheVersion = contextGeneration,
                Accounts = await GetAccountsAsync(user.Id, canAccessAllAccounts, user.IsIntern())
            };


            // _logger.LogInformation("User account context phase=accounts-complete elapsedMs={ElapsedMs} accounts={AccountCount}", stopwatch.ElapsedMilliseconds, context.Accounts?.Count ?? 0);

            SetCachedContext(normalizedUserId, contextGeneration, context);
            // _logger.LogInformation(
            //     "User account context source=db user={UserId} isAdminOrViewer={IsAdminOrViewer} defaultAccountId={DefaultAccountId} accounts={AccountCount}",
            //     normalizedUserId,
            //     context.IsAdminOrViewer,
            //     context.DefaultAccountId,
            //     context.Accounts?.Count ?? 0);

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build user account context for identifier {UserId}", normalizedUserId);
            return null;
        }
    }

    public UserAccountContextAccount? ResolveSelectedAccount(UserAccountContext context, string? preferredIdentity)
    {
        if (context.Accounts == null || context.Accounts.Count == 0)
        {
            return null;
        }

        var selected = !string.IsNullOrWhiteSpace(preferredIdentity)
            ? context.Accounts.FirstOrDefault(a =>
                // Intern selectors retain InternName as their value because a shared fund can contain multiple interns' donation data.
                string.Equals(
                    string.IsNullOrWhiteSpace(a.InternName) ? a.Fund : a.InternName,
                    preferredIdentity,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Fund, preferredIdentity, StringComparison.OrdinalIgnoreCase))
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
            lock (ContextCacheSync)
            {
                var normalizedUserId = userId.Trim();
                if (ContextCache.Remove(normalizedUserId, out var cached))
                {
                    ContextCacheLru.Remove(cached.LruNode);
                }
            }
        }
    }

    private static bool TryGetCachedContext(
        string userId,
        long generation,
        bool isAdminOrViewer,
        out UserAccountContext context)
    {
        lock (ContextCacheSync)
        {
            if (ContextCache.TryGetValue(userId, out var cached)
                && cached.Context.CacheVersion == generation
                && cached.Context.IsAdminOrViewer == isAdminOrViewer)
            {
                ContextCacheLru.Remove(cached.LruNode);
                ContextCacheLru.AddLast(cached.LruNode);
                context = CloneContext(cached.Context);
                return true;
            }

            if (cached is not null)
            {
                ContextCache.Remove(userId);
                ContextCacheLru.Remove(cached.LruNode);
            }
        }

        context = default!;
        return false;
    }

    private static void SetCachedContext(string userId, long generation, UserAccountContext context)
    {
        lock (ContextCacheSync)
        {
            if (ContextCache.Remove(userId, out var existing))
            {
                ContextCacheLru.Remove(existing.LruNode);
            }

            var node = new LinkedListNode<string>(userId);
            ContextCacheLru.AddLast(node);
            ContextCache[userId] = new CachedContext(CloneContext(context), node);

            while (ContextCache.Count > MaxContextCacheEntries)
            {
                var oldest = ContextCacheLru.First;
                if (oldest is null)
                {
                    break;
                }

                ContextCacheLru.RemoveFirst();
                ContextCache.Remove(oldest.Value);
            }
        }
    }

    private sealed record CachedContext(UserAccountContext Context, LinkedListNode<string> LruNode);

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

    private async Task<List<UserAccountContextAccount>> GetAccountsAsync(int userId, bool isAdminOrViewer, bool isIntern)
    {
        if (_fundReferenceService is null || isIntern)
        {
            // Intern users must resolve their own account directly so the context preserves the intern lookup identity.
            var legacyAccounts = isAdminOrViewer
                ? await _accountRepository.GetAllAsync()
                : await _userAccountAccessRepository.GetUserAccountsAsync(userId);
            return legacyAccounts.Select(ToContextAccount).ToList();
        }

        var references = await _fundReferenceService.GetAllAsync();
        return _fundReferenceService.FilterForUser(references, userId, isAdminOrViewer)
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
            OtherFunds = a.OtherFunds,
            InternName = a.InternName,
            DisplayName = a.DisplayName
        }).ToList();
    }

    private static UserAccountContextAccount ToContextAccount(FundReferenceDto account) => new()
    {
        AccountId = account.AccountId,
        Fund = account.Fund,
        AccountingClass = account.AccountingClass,
        AccountNumber = account.AccountNumber,
        Overhead = account.Overhead,
        SoftCredit = account.SoftCredit,
        BalanceAdjustment = account.BalanceAdjustment,
        OtherFunds = account.OtherFunds,
        InternName = account.InternName,
        DisplayName = account.DisplayName
    };

    private static UserAccountContextAccount ToContextAccount(Cya2.Core.Entities.Account account) => new()
    {
        AccountId = account.AccountId,
        Fund = account.Fund,
        AccountingClass = account.AccountingClass,
        AccountNumber = account.AccountNumber,
        CreatedAt = account.CreatedAt,
        Overhead = account.Overhead,
        SoftCredit = account.SoftCredit,
        BalanceAdjustment = account.BalanceAdjustment,
        OtherFunds = account.OtherFunds,
        // Derive the lookup identity from the intern account convention; AccountingClass is intentionally irrelevant for intern accounts.
        InternName = InternAccountUtility.TryGetInternDesignationName(account.Fund, out var internName)
            ? internName
            : string.Empty,
        DisplayName = InternAccountUtility.TryGetInternDesignationName(account.Fund, out internName)
            ? internName
            : account.Fund
    };

    private static UserAccountContext CloneContext(UserAccountContext source)
    {
        return new UserAccountContext
        {
            UserId = source.UserId,
            IsAdminOrViewer = source.IsAdminOrViewer,
            IsViewer = source.IsViewer,
            IsIntern = source.IsIntern,
            InternName = source.InternName,
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
                    OtherFunds = a.OtherFunds,
                    InternName = a.InternName,
                    DisplayName = a.DisplayName,
                })
                .ToList()
        };
    }
}
