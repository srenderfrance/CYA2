using System.Security.Claims;
using Cya2.Application.Interfaces;

namespace Cya2.Infrastructure.Services;

public class UserIdResolver : IUserIdResolver
{
    public string ResolveUserId(ClaimsPrincipal user, string? fallbackCurrentUserId = null)
    {
        if (user == null) return fallbackCurrentUserId ?? string.Empty;

        var resolved = user.FindFirstValue("UserId")
                       ?? user.FindFirstValue(ClaimTypes.Email)
                       ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? user.Identity?.Name
                       ?? string.Empty;

        if (string.IsNullOrWhiteSpace(resolved) && !string.IsNullOrWhiteSpace(fallbackCurrentUserId))
        {
            resolved = fallbackCurrentUserId;
        }

        return resolved ?? string.Empty;
    }
}
