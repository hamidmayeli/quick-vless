using API.Services;
using API.Services.Xray;
using API.Storage;

namespace API.Endpoints;

public static class ConfigEndpoints
{
    private sealed class Log { }

    public static void MapConfigEndpoints(this IEndpointRouteBuilder builder)
    {
        // Public: returns the client link(s) for a user by ID, rendered from the active profile's template.
        // If single_connection is true, rotates the secret to kick old connections.
        builder.MapGet("/config/{userId}", async (
            string userId,
            UserRepository repo,
            XrayRuntime xray,
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

            // Checked before rotating, so a user's secret never changes for a link we can't hand out.
            if (!generator.CanGenerate)
            {
                logger.LogError("GET /config/{UserId} — no valid Xray profile is applied", userId);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (user.SingleConnection)
            {
                var oldSecret = user.Secret;
                user.Secret = Guid.NewGuid().ToString();
                await repo.UpdateAsync(user);
                await xray.SecretRotatedAsync(user, oldSecret);
                logger.LogInformation("GET /config/{UserId} — rotated secret for single-connection user {Name}", userId, user.Name);
            }

            var url = generator.Generate(user.Secret, user.Name);
            if (url is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            logger.LogInformation("GET /config/{UserId} — returning config for {Name}", userId, user.Name);
            return Results.Text(url, "text/plain");
        });
    }
}
