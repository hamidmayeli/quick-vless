using API.Models;
using API.Services;
using API.Storage;

namespace API.Endpoints;

public static class UsersEndpoints
{
    private sealed class Log { }

    public static void MapUsersEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/users").RequireAuthorization();

        group.MapGet("/", async (UserRepository repo, ILogger<Log> logger) =>
        {
            var users = await repo.GetAllAsync();
            logger.LogInformation("GET /users — {Count} users returned", users.Count);
            return Results.Ok(users);
        });

        group.MapPost("/", async (
            CreateUserRequest req,
            UserRepository repo,
            XrayService xray,
            ILogger<Log> logger) =>
        {
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Name = req.Name,
                Secret = Guid.NewGuid().ToString(),
                Quota = req.Quota,
                Expiry = req.Expiry,
                SingleConnection = req.SingleConnection,
                Enabled = req.Enabled,
            };
            await repo.AddAsync(user);
            if (user.Enabled) await xray.AddUserAsync(user);
            logger.LogInformation("POST /users — created user {Name} id={Id} enabled={Enabled}", user.Name, user.Id, user.Enabled);
            return Results.Created($"/users/{user.Id}", user);
        });

        group.MapPut("/{id}", async (
            string id,
            UpdateUserRequest req,
            UserRepository repo,
            XrayService xray,
            ILogger<Log> logger) =>
        {
            logger.LogDebug("PUT /users/{Id}", id);
            var existing = await repo.GetByIdAsync(id);
            if (existing is null)
            {
                logger.LogWarning("PUT /users/{Id} — not found", id);
                return Results.NotFound();
            }

            var wasEnabled = existing.Enabled;
            existing.Name = req.Name ?? existing.Name;
            existing.Quota = req.Quota;
            existing.Expiry = req.Expiry;
            existing.SingleConnection = req.SingleConnection ?? existing.SingleConnection;
            existing.Enabled = req.Enabled ?? existing.Enabled;
            await repo.UpdateAsync(existing);

            if (!wasEnabled && existing.Enabled) await xray.AddUserAsync(existing);
            else if (wasEnabled && !existing.Enabled) await xray.RemoveUserAsync(existing);

            logger.LogInformation("PUT /users/{Id} — updated, enabled={Enabled}", id, existing.Enabled);
            return Results.Ok(existing);
        });

        group.MapDelete("/{id}", async (
            string id,
            UserRepository repo,
            XrayService xray,
            ILogger<Log> logger) =>
        {
            logger.LogDebug("DELETE /users/{Id}", id);
            var user = await repo.GetByIdAsync(id);
            if (user is null)
            {
                logger.LogWarning("DELETE /users/{Id} — not found", id);
                return Results.NotFound();
            }
            if (user.Enabled) await xray.RemoveUserAsync(user);
            await repo.DeleteAsync(id);
            logger.LogInformation("DELETE /users/{Id} — deleted {Name}", id, user.Name);
            return Results.NoContent();
        });
    }

    public record CreateUserRequest(
        string Name,
        double? Quota,
        DateOnly? Expiry,
        bool SingleConnection,
        bool Enabled);

    public record UpdateUserRequest(
        string? Name,
        double? Quota,
        DateOnly? Expiry,
        bool? SingleConnection,
        bool? Enabled);
}
