using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using API.Storage;

namespace API.Auth;

public static class AdminTokenValidator
{
    // Runs after the JWT signature and lifetime checks. The stored admin is the source of truth:
    // deleted or pending admins are rejected, and the role claim is replaced with the stored role.
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity || identity.Name is null)
        {
            context.Fail("Token has no admin name");
            return;
        }

        var admins = context.HttpContext.RequestServices.GetRequiredService<AdminRepository>();
        var admin = await admins.GetByUsernameAsync(identity.Name);
        if (admin is null || admin.IsPending)
        {
            context.Fail("Admin no longer active");
            return;
        }

        foreach (var claim in identity.FindAll(identity.RoleClaimType).ToList())
            identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(identity.RoleClaimType, admin.Role.ToClaimValue()));
    }
}
