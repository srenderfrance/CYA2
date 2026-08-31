using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;

namespace Cya2.Application.Services;

public sealed class GoogleSignInService : IGoogleSignInService
{
    private readonly IUserRepository _userRepository;

    public GoogleSignInService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<GoogleSignInResult> ResolveUserAsync(string googleId, string email)
    {
        var user = await _userRepository.GetByExternalIdAsync(googleId);
        if (user is null)
        {
            var emailMatch = await _userRepository.GetByEmailAsync(email);
            if (emailMatch is null)
            {
                return new GoogleSignInResult(null, "User not registered");
            }

            if (!string.IsNullOrWhiteSpace(emailMatch.GoogleId) &&
                !string.Equals(emailMatch.GoogleId, googleId, StringComparison.Ordinal))
            {
                return new GoogleSignInResult(null, "Google ID mismatch");
            }

            if (string.IsNullOrWhiteSpace(emailMatch.GoogleId))
            {
                emailMatch.GoogleId = googleId;
                await _userRepository.UpdateAsync(emailMatch);
            }

            user = emailMatch;
        }
        else if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            return new GoogleSignInResult(null, "Email mismatch for Google ID");
        }

        return new GoogleSignInResult(user);
    }
}
