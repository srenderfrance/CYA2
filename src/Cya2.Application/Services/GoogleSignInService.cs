using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace Cya2.Application.Services;

public sealed class GoogleSignInService : IGoogleSignInService
{
    private readonly IUserRepository _userRepository;
    private readonly InitialAdminBootstrapOptions _bootstrapOptions;

    public GoogleSignInService(
        IUserRepository userRepository,
        IOptions<InitialAdminBootstrapOptions> bootstrapOptions)
    {
        _userRepository = userRepository;
        _bootstrapOptions = bootstrapOptions.Value;
    }

    public async Task<GoogleSignInResult> ResolveUserAsync(string googleId, string email)
    {
        var user = await _userRepository.GetByExternalIdAsync(googleId);
        if (user is null)
        {
            var emailMatch = await _userRepository.GetByEmailAsync(email);
            if (emailMatch is null)
            {
                if (_bootstrapOptions.IsEnabled &&
                    string.Equals(_bootstrapOptions.Email.Trim(), email, StringComparison.OrdinalIgnoreCase))
                {
                    var initialAdmin = await _userRepository.TryCreateInitialAdminAsync(new User(
                        googleId,
                        email,
                        _bootstrapOptions.Name,
                        "en",
                        "Admin",
                        null));

                    if (initialAdmin is not null)
                    {
                        return new GoogleSignInResult(initialAdmin);
                    }
                }

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
