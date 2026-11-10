using System.Globalization;
using System.Security.Claims;
using Cya2.Core.Interfaces;

namespace cya2.Services;

public enum SessionValidationResult
{
    Valid,
    Expired,
    Revoked,
    Unavailable
}

public sealed class SessionValidationService(IUserRepository users, TimeProvider clock, ILogger<SessionValidationService> logger)
{
    public const string ExpiresClaim = "cya2.session.expires";

    public async Task<SessionValidationResult> ValidateAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !long.TryParse(principal.FindFirstValue(ExpiresClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
            return SessionValidationResult.Revoked;

        if (clock.GetUtcNow().ToUnixTimeSeconds() >= expires)
            return SessionValidationResult.Expired;

        if (!int.TryParse(principal.FindFirstValue("UserId"), NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
            return SessionValidationResult.Revoked;

        try
        {
            var user = await users.GetByIdAsync(userId);
            var role = principal.FindFirstValue("AuthLevel");
            return user is not null && !string.IsNullOrWhiteSpace(role) &&
                   string.Equals(user.AuthLevel?.Trim(), role.Trim(), StringComparison.OrdinalIgnoreCase)
                ? SessionValidationResult.Valid
                : SessionValidationResult.Revoked;
        }
        catch (Exception)
        {
            logger.LogWarning("Session revalidation could not reach the user store; validation will be retried.");
            return SessionValidationResult.Unavailable;
        }
    }
}
