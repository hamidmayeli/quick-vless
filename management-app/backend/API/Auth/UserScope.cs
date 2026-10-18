using System.Security.Claims;
using API.Models;

namespace API.Auth;

public static class UserScope
{
    // The super admin sees every user, including legacy users without an owner.
    // Other admins only see the users they created.
    public static bool IsVisibleTo(User user, string username, bool isSuperAdmin) =>
        isSuperAdmin || string.Equals(user.CreatedBy, username, StringComparison.OrdinalIgnoreCase);

    public static bool CanSee(this ClaimsPrincipal principal, User user) =>
        IsVisibleTo(user, principal.GetUsername(), principal.IsSuperAdmin());
}
