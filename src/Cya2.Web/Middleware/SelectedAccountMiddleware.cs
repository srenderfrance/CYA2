using Cya2.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System.Threading.Tasks;

namespace cya2.Middleware
{
    public class SelectedAccountMiddleware
    {
        private readonly RequestDelegate _next;

        public SelectedAccountMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IUserSelectionService userSelectionService, Cya2.Application.Interfaces.ISessionUserStateService session, Cya2.Application.Interfaces.IUserIdResolver userIdResolver)
        {
            try
            {
                // Only populate on non-auth requests where user is authenticated
                if (context.User?.Identity?.IsAuthenticated == true)
                {
                    // Resolve user id using same resolver the components use to ensure consistent keys
                    var userId = userIdResolver.ResolveUserId(context.User, session.CurrentUserId);

                    if (!string.IsNullOrWhiteSpace(userId))
                    {
                        // Ensure the session knows which user it's for so ResetForUser called by components won't clear our value
                        session.CurrentUserId = userId;

                        if (string.IsNullOrWhiteSpace(session.SelectedAccountFund))
                        {
                            if (userSelectionService.TryGetSelectedAccount(userId, out var acct))
                            {
                                session.SelectedAccountFund = acct;
                            }
                        }
                    }
                }
            }
            catch { }

            await _next(context);
        }
    }

    public static class SelectedAccountMiddlewareExtensions
    {
        public static IApplicationBuilder UseSelectedAccountMiddleware(this IApplicationBuilder app)
        {
            return app.UseMiddleware<SelectedAccountMiddleware>();
        }
    }
}
