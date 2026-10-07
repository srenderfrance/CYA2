namespace Cya2.Application.Interfaces;

public interface IAdminAuthorizationContext
{
    Task<bool> IsCurrentUserAdminAsync();
}
