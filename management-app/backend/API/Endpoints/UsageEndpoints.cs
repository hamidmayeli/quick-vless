using API.Storage;

namespace API.Endpoints;

public static class UsageEndpoints
{
    private sealed class Log { }

    public static void MapUsageEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/usage", async (UsageRepository repo, ILogger<Log> logger) =>
        {
            var records = await repo.GetAllAsync();
            logger.LogInformation("GET /usage — {Count} records returned", records.Count);
            return Results.Ok(records);
        }).RequireAuthorization();
    }
}
