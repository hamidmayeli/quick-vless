using System.Security.Claims;

namespace API.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string GetUsername(this ClaimsPrincipal principal) =>
        principal.Identity?.Name ?? throw new InvalidOperationException("Authenticated principal has no name claim");

    public static bool IsSuperAdmin(this ClaimsPrincipal principal) =>
        principal.IsInRole(AdminRoles.SuperAdmin);
}
