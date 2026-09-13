using API.Auth;
using API.Storage;

namespace API.Endpoints;

public static class AuthEndpoints
{
    private sealed class Log { }

    public static void MapAuthEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost(
            "/auth/login",
            async (
                LoginRequest req,
                AdminRepository admins,
                TokenService tokens,
                ILogger<Log> logger) =>
            {
                logger.LogDebug("POST /auth/login username={Username}", req.Username);

                var admin = await admins.GetByUsernameAsync(req.Username);
                if (admin is null)
                {
                    var existing = await admins.GetAllAsync();
                    if (existing.Count == 0)
                    {
                        logger.LogInformation("No admins exist — creating first admin: {Username}", req.Username);
                        var (hash, salt) = tokens.HashPassword(req.Password);
                        admin = new()
                        {
                            Username = req.Username,
                            PasswordHash = hash,
                            PasswordSalt = salt,
                        };
                        await admins.UpdateAsync(admin);
                    }
                    else
                    {
                        logger.LogWarning("Unknown username {Username} rejected", req.Username);
                        return Results.Unauthorized();
                    }
                }
                else if (!tokens.VerifyPassword(admin.PasswordHash, admin.PasswordSalt, req.Password))
                {
                    logger.LogWarning("Invalid password for {Username}", req.Username);
                    return Results.Unauthorized();
                }

                var access = tokens.CreateAccessToken(admin.Username);
                var (refresh, expiry) = tokens.CreateRefreshToken();
                admin.RefreshToken = refresh;
                admin.RefreshTokenExpiry = expiry;
                await admins.UpdateAsync(admin);

                logger.LogInformation("Login successful for {Username}", admin.Username);
                return Results.Ok(new TokenResponse(access, refresh));
            });

        builder.MapPost("/auth/refresh", async (
            RefreshRequest req,
            AdminRepository admins,
            TokenService tokens,
            ILogger<Log> logger) =>
        {
            logger.LogDebug("POST /auth/refresh username={Username}", req.Username);

            var admin = await admins.GetByUsernameAsync(req.Username);
            if (admin is null || !tokens.ValidateRefreshToken(admin, req.RefreshToken))
            {
                logger.LogWarning("Refresh token invalid or expired for {Username}", req.Username);
                return Results.Unauthorized();
            }

            var access = tokens.CreateAccessToken(admin.Username);
            var (refresh, expiry) = tokens.CreateRefreshToken();
            admin.RefreshToken = refresh;
            admin.RefreshTokenExpiry = expiry;
            await admins.UpdateAsync(admin);

            logger.LogInformation("Token refreshed for {Username}", admin.Username);
            return Results.Ok(new TokenResponse(access, refresh));
        });
    }

    public record LoginRequest(string Username, string Password);
    public record RefreshRequest(string Username, string RefreshToken);
    public record TokenResponse(string AccessToken, string RefreshToken);
}
