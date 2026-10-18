using API.Models;

namespace API.Auth;

public static class AdminRoles
{
    public const string SuperAdmin = "super_admin";
    public const string Admin = "admin";
    public const string SuperAdminPolicy = "SuperAdmin";

    public static string ToClaimValue(this AdminRole role) =>
        role == AdminRole.SuperAdmin ? SuperAdmin : Admin;
}
