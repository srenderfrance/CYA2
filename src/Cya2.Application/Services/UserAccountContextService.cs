using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public class UserAccountContextService : IUserAccountContextService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserAccountAccessRepository _userAccountAccessRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ILogger<UserAccountContextService> _logger;

    public UserAccountContextService(
        IUserRepository userRepository,
        IUserAccountAccessRepository userAccountAccessRepository,
        IAccountRepository accountRepository,
        ILogger<UserAccountContextService> logger)
    {
        _userRepository = userRepository;
        _userAccountAccessRepository = userAccountAccessRepository;
        _accountRepository = accountRepository;
        _logger = logger;
    }

    public async Task<UserAccountContext?> GetContextAsync(string userId, bool isAdminOrViewerHint = false)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        try
        {
            var user = await ResolveUserAsync(userId);
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
                _logger.LogWarning("Applying admin/viewer hint for user {UserId} with DB AuthLevel '{AuthLevel}'", userId, authLevel);
            }

            var context = new UserAccountContext
            {
                UserId = user.Id,
                IsAdminOrViewer = canAccessAllAccounts,
                DefaultAccountId = user.DefaultAccount,
                Accounts = await GetAccountsAsync(user.Id, canAccessAllAccounts)
            };

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build user account context for identifier {UserId}", userId);
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
}
