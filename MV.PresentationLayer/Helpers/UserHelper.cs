using System.Security.Claims;
using MV.DomainLayer.Constants;

namespace MV.PresentationLayer.Helpers;

public static class UserHelper
{
    public static string GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst(ApplicationClaimTypes.Id)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token.");
    }

    public static string GetUserEmail(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Email)?.Value
            ?? throw new UnauthorizedAccessException("User Email not found in token.");
    }

    public static string GetUserRole(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Role)?.Value
            ?? throw new UnauthorizedAccessException("User Role not found in token.");
    }
}
