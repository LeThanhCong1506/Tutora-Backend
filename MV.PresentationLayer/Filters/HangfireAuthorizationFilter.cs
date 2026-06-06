using Hangfire.Dashboard;
using MV.DomainLayer.Constants;

namespace MV.PresentationLayer.Filters;

/// <summary>
/// Authorization filter for Hangfire Dashboard
/// In production, implement proper authentication/authorization
/// </summary>
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        // Development: allow all access.
        // Production: require authenticated admin — enforced by the #else branch below.

#if DEBUG
        return true; // Allow in development
#else
        // In production, implement proper auth
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole(UserRole.Admin);
#endif
    }
}
