using System.Security.Claims;
using API.Auth;
using API.Storage;

namespace API.Endpoints;

public static class UsageEndpoints
{
    private sealed class Log { }

    public static void MapUsageEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/usage", async (
            ClaimsPrincipal me,
            UsageRepository usageRepo,
            UserRepository userRepo,
            ILogger<Log> logger) =>
        {
            var records = await usageRepo.GetAllAsync();
            if (!me.IsSuperAdmin())
            {
                var ownUserIds = (await userRepo.GetAllAsync())
                    .Where(me.CanSee)
                    .Select(u => u.Id)
                    .ToHashSet();
                records = records.Where(r => ownUserIds.Contains(r.UserId)).ToList();
            }
            logger.LogInformation("GET /usage — {Count} records returned to {Admin}", records.Count, me.GetUsername());
            return Results.Ok(records);
        }).RequireAuthorization();
    }
}
