using API.Services;
using API.Storage;

namespace API.Endpoints;

public static class ConfigEndpoints
{
    private sealed class Log { }

    public static void MapConfigEndpoints(this IEndpointRouteBuilder builder)
    {
        // Public: returns vless:// URL for a user by ID.
        // If single_connection is true, rotates the secret to kick old connections.
        builder.MapGet("/config/{userId}", async (
            string userId,
            UserRepository repo,
            XrayService xray,
            VlessUrlGenerator generator,
            ILogger<Log> logger) =>
        {
            logger.LogDebug("GET /config/{UserId}", userId);
            var user = await repo.GetByIdAsync(userId);
            if (user is null || !user.Enabled)
            {
                logger.LogWarning("GET /config/{UserId} — not found or disabled", userId);
                return Results.NotFound();
            }

            if (user.SingleConnection)
            {
                var oldSecret = user.Secret;
                user.Secret = Guid.NewGuid().ToString();
                await repo.UpdateAsync(user);
                await xray.RemoveUserAsync(new API.Models.User { Id = user.Id, Name = user.Name, Secret = oldSecret, Enabled = true, SingleConnection = true });
                await xray.AddUserAsync(user);
                logger.LogInformation("GET /config/{UserId} — rotated secret for single-connection user {Name}", userId, user.Name);
            }

            var url = generator.Generate(user.Secret, user.Name);
            logger.LogInformation("GET /config/{UserId} — returning config for {Name}", userId, user.Name);
            return Results.Text(url, "text/plain");
        });
    }
}
