using API.Auth;
using API.Models;
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

                if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                {
                    logger.LogWarning("Login rejected — blank username or password");
                    return Results.BadRequest();
                }

                var admin = await admins.GetByUsernameAsync(req.Username);
                if (admin is null)
                {
                    var (hash, salt) = tokens.HashPassword(req.Password);
                    admin = await admins.CreateFirstSuperAdminAsync(req.Username, hash, salt);
                    if (admin is null)
                    {
                        logger.LogWarning("Unknown username {Username} rejected", req.Username);
                        return Results.Unauthorized();
                    }
                    logger.LogInformation("No admins exist — created super admin {Username}", admin.Username);
                }
                else if (admin.IsPending)
                {
                    var (hash, salt) = tokens.HashPassword(req.Password);
                    var claimed = await admins.SetInitialPasswordAsync(admin.Username, hash, salt);
                    if (claimed is not null)
                    {
                        admin = claimed;
                        logger.LogInformation("Initial password set for {Username}", admin.Username);
                    }
                    else
                    {
                        // Another login set the password first — treat this one as a normal login.
                        admin = await admins.GetByUsernameAsync(req.Username);
                        if (admin is null || !IsValidPassword(tokens, admin, req.Password))
                        {
                            logger.LogWarning("Invalid password for {Username}", req.Username);
                            return Results.Unauthorized();
                        }
                    }
                }
                else if (!IsValidPassword(tokens, admin, req.Password))
                {
                    logger.LogWarning("Invalid password for {Username}", req.Username);
                    return Results.Unauthorized();
                }

                var access = tokens.CreateAccessToken(admin.Username, admin.Role);
                var (refresh, expiry) = tokens.CreateRefreshToken();
                if (!await admins.AddRefreshTokenAsync(admin.Username, new() { Token = refresh, Expiry = expiry }, revokeToken: null))
                {
                    logger.LogWarning("Admin {Username} was removed during login", admin.Username);
                    return Results.Unauthorized();
                }

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
            if (admin is null || admin.IsPending || refreshToken is null || !tokens.ValidateRefreshToken(admin, refreshToken))
            {
                logger.LogWarning("Refresh token invalid or expired for {Username}", req.Username);
                return Results.Unauthorized();
            }

            var access = tokens.CreateAccessToken(admin.Username, admin.Role);
            var (refresh, expiry) = tokens.CreateRefreshToken();
            if (!await admins.AddRefreshTokenAsync(admin.Username, new() { Token = refresh, Expiry = expiry }, revokeToken: refreshToken))
            {
                logger.LogWarning("Admin {Username} was removed during refresh", admin.Username);
                return Results.Unauthorized();
            }

            logger.LogInformation("Token refreshed for {Username}", admin.Username);
            return TokenResponseWithRefreshCookie(access, refresh, expiry);
        });
    }

    private static bool IsValidPassword(TokenService tokens, Admin admin, string password) =>
        admin is { PasswordHash: not null, PasswordSalt: not null }
        && tokens.VerifyPassword(admin.PasswordHash, admin.PasswordSalt, password);

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
