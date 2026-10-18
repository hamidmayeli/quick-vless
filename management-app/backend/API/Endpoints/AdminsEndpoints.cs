using System.Security.Claims;
using System.Text.RegularExpressions;
using API.Auth;
using API.Models;
using API.Storage;

namespace API.Endpoints;

public static partial class AdminsEndpoints
{
    private sealed class Log { }

    [GeneratedRegex(@"^[A-Za-z0-9._@-]{1,64}$")]
    private static partial Regex UsernamePattern();

    public static void MapAdminsEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/admins").RequireAuthorization(AdminRoles.SuperAdminPolicy);

        group.MapGet("/", async (AdminRepository admins, UserRepository users, ILogger<Log> logger) =>
        {
            var all = await admins.GetAllAsync();
            var userCounts = await CountUsersByOwnerAsync(users);
            var response = all
                .OrderByDescending(a => a.Role == AdminRole.SuperAdmin)
                .ThenBy(a => a.Username, StringComparer.OrdinalIgnoreCase)
                .Select(a => AdminResponse.From(a, userCounts.GetValueOrDefault(a.Username)))
                .ToList();
            logger.LogInformation("GET /admins — {Count} admins returned", response.Count);
            return Results.Ok(response);
        });

        group.MapPost("/", async (
            CreateAdminRequest req,
            ClaimsPrincipal me,
            AdminRepository admins,
            ILogger<Log> logger) =>
        {
            var username = req.Username?.Trim() ?? string.Empty;
            if (!UsernamePattern().IsMatch(username))
            {
                logger.LogWarning("POST /admins — invalid username rejected");
                return Results.BadRequest();
            }

            var admin = new Admin { Username = username, Role = AdminRole.Admin };
            if (!await admins.AddAsync(admin))
            {
                logger.LogWarning("POST /admins — username {Username} already exists", username);
                return Results.Conflict();
            }

            logger.LogInformation("POST /admins — {Actor} created admin {Username}", me.GetUsername(), username);
            return Results.Created($"/admins/{Uri.EscapeDataString(username)}", AdminResponse.From(admin, userCount: 0));
        });

        group.MapDelete("/{username}", async (
            string username,
            ClaimsPrincipal me,
            AdminRepository admins,
            UserRepository users,
            ILogger<Log> logger) =>
        {
            var admin = await admins.GetByUsernameAsync(username);
            if (admin is null)
            {
                logger.LogWarning("DELETE /admins/{Username} — not found", username);
                return Results.NotFound();
            }
            if (admin.Role == AdminRole.SuperAdmin)
            {
                logger.LogWarning("DELETE /admins/{Username} — the super admin cannot be deleted", username);
                return Results.BadRequest();
            }

            // Hand the admin's VPN users to the super admin so their clients keep working.
            var reassigned = await users.ReassignOwnerAsync(admin.Username, me.GetUsername());
            await admins.DeleteAsync(admin.Username);
            logger.LogInformation("DELETE /admins/{Username} — deleted by {Actor}, {Count} users reassigned",
                admin.Username, me.GetUsername(), reassigned);
            return Results.NoContent();
        });
    }

    private static async Task<Dictionary<string, int>> CountUsersByOwnerAsync(UserRepository users) =>
        (await users.GetAllAsync())
            .Where(u => u.CreatedBy is not null)
            .GroupBy(u => u.CreatedBy!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    public enum AdminStatus { Pending, Active }

    public record AdminResponse(string Username, AdminRole Role, AdminStatus Status, int UserCount)
    {
        public static AdminResponse From(Admin admin, int userCount) =>
            new(admin.Username, admin.Role, admin.IsPending ? AdminStatus.Pending : AdminStatus.Active, userCount);
    }

    public record CreateAdminRequest(string? Username);
}
