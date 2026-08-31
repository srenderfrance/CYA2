using Cya2.Core.Entities;

namespace Cya2.Application.Interfaces;

public interface IGoogleSignInService
{
    Task<GoogleSignInResult> ResolveUserAsync(string googleId, string email);
}

public sealed record GoogleSignInResult(User? User, string? RejectionReason = null)
{
    public bool IsAuthorized => User is not null;
}
