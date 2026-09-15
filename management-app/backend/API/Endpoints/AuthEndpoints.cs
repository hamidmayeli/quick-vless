using API.Auth;
using API.Storage;

namespace API.Endpoints;

public static class AuthEndpoints
{
    private const string RefreshCookieName = "refresh_token";
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
                admin.RefreshTokens.RemoveAll(rt => rt.Expiry <= DateTime.UtcNow);
                admin.RefreshTokens.Add(new() { Token = refresh, Expiry = expiry });
                await admins.UpdateAsync(admin);

                logger.LogInformation("Login successful for {Username}", admin.Username);
                return TokenResponseWithRefreshCookie(access, refresh, expiry);
            });

        builder.MapPost("/auth/refresh", async (
            RefreshRequest req,
            HttpRequest httpRequest,
            AdminRepository admins,
            TokenService tokens,
            ILogger<Log> logger) =>
        {
            logger.LogDebug("POST /auth/refresh username={Username}", req.Username);

            httpRequest.Cookies.TryGetValue(RefreshCookieName, out var refreshToken);
            var admin = await admins.GetByUsernameAsync(req.Username);
            if (admin is null || refreshToken is null || !tokens.ValidateRefreshToken(admin, refreshToken))
            {
                logger.LogWarning("Refresh token invalid or expired for {Username}", req.Username);
                return Results.Unauthorized();
            }

            var access = tokens.CreateAccessToken(admin.Username);
            var (refresh, expiry) = tokens.CreateRefreshToken();
            admin.RefreshTokens.RemoveAll(rt => rt.Expiry <= DateTime.UtcNow || rt.Token == refreshToken);
            admin.RefreshTokens.Add(new() { Token = refresh, Expiry = expiry });
            await admins.UpdateAsync(admin);

            logger.LogInformation("Token refreshed for {Username}", admin.Username);
            return TokenResponseWithRefreshCookie(access, refresh, expiry);
        });
    }

    private static IResult TokenResponseWithRefreshCookie(string accessToken, string refreshToken, DateTime expiry)
    {
        var response = Results.Ok(new TokenResponse(accessToken));
        return new CookieResult(response, refreshToken, expiry);
    }

    private sealed class CookieResult(IResult inner, string refreshToken, DateTime expiry) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Cookies.Append(RefreshCookieName, refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = expiry,
                Path = "/api/v1/auth",
            });
            await inner.ExecuteAsync(httpContext);
        }
    }

    public record LoginRequest(string Username, string Password);
    public record RefreshRequest(string Username);
    public record TokenResponse(string AccessToken);
}
