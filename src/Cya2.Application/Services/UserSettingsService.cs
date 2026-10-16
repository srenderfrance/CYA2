using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public class UserSettingsService : IUserSettingsService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserAccountAccessRepository _userAccountAccessRepository;
    private readonly ILogger<UserSettingsService> _logger;
    private readonly IUserAccountContextService _userAccountContextService;

    public UserSettingsService(
        IUserRepository userRepository,
        IUserAccountAccessRepository userAccountAccessRepository,
        ILogger<UserSettingsService> logger,
        IUserAccountContextService userAccountContextService)
    {
        _userRepository = userRepository;
        _userAccountAccessRepository = userAccountAccessRepository;
        _logger = logger;
        _userAccountContextService = userAccountContextService;
    }

    public async Task<UserSettingsDto?> GetUserSettingsAsync(string userIdentifier, bool isAdminHint = false)
    {
        try
        {
            var context = await _userAccountContextService.GetContextAsync(userIdentifier, isAdminHint);
            if (context == null)
            {
                return null;
            }

            var user = await _userRepository.GetByIdAsync(context.UserId);
            var language = user?.Language;

            var settings = new UserSettingsDto
            {
                UserId = context.UserId,
                DefaultAccountId = context.DefaultAccountId,
                Language = string.IsNullOrWhiteSpace(language) ? "en-US" : language,
                UserAccounts = context.Accounts.Select(a => new AccountOptionDto
                {
                    AccountId = a.AccountId,
                    Fund = a.Fund ?? string.Empty,
                    AccountingClass = a.AccountingClass ?? string.Empty,
                    AccountNumber = a.AccountNumber ?? string.Empty,
                    Overhead = Convert.ToDecimal(a.Overhead)
                }).ToList()
            };

            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user settings for {UserIdentifier}", userIdentifier);
            return null;
        }
    }

    public async Task<bool> UpdateDefaultAccountAsync(int userId, int? defaultAccountId)
    {
        try
        {
            var updated = await _userAccountAccessRepository.SetUserDefaultAccountAsync(userId, defaultAccountId);
            if (updated)
            {
                _userAccountContextService.Invalidate(userId.ToString());
            }

            return updated;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update default account for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> UpdateLanguageAsync(int userId, string language)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                return false;
            }

            user.Language = string.Equals(language, "es-US", StringComparison.OrdinalIgnoreCase) ? "es-US" : "en-US";
            var updated = await _userRepository.UpdateAsync(user);

            var success = updated != null && updated.Id == userId;
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update language for user {UserId}", userId);
            return false;
        }
    }

}
