using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

public interface IUserSettingsService
{
    Task<UserSettingsDto?> GetUserSettingsAsync(string userIdentifier, bool isAdminHint = false);
    Task<bool> UpdateDefaultAccountAsync(int userId, int? defaultAccountId);
    Task<bool> UpdateLanguageAsync(int userId, string language);
}
