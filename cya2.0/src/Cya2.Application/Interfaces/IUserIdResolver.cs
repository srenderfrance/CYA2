using System.Security.Claims;

namespace Cya2.Application.Interfaces;

public interface IUserIdResolver
{
    string ResolveUserId(ClaimsPrincipal user, string? fallbackCurrentUserId = null);
}
