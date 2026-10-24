using API.Auth;
using API.Storage;

namespace API.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/settings").RequireAuthorization();

        group.MapGet("/", async (SettingsRepository settings) =>
        {
            var current = await settings.GetAsync();
            return Results.Ok(new SettingsResponse(current.CustomDomain));
        });

        group.MapPut("/", async (UpdateSettingsRequest request, SettingsRepository settings) =>
        {
            var domain = request.CustomDomain?.Trim();
            if (!string.IsNullOrEmpty(domain))
            {
                if (!Uri.TryCreate($"https://{domain}", UriKind.Absolute, out var uri)
                    || uri.AbsolutePath != "/"
                    || !string.IsNullOrEmpty(uri.Query)
                    || !string.IsNullOrEmpty(uri.Fragment)
                    || !string.IsNullOrEmpty(uri.UserInfo))
                    return Results.BadRequest(new { error = "Enter a hostname, optionally with a port, without a scheme or path." });

                domain = uri.Authority;
            }

            await settings.UpdateAsync(current => current.CustomDomain = string.IsNullOrEmpty(domain) ? null : domain);
            return Results.Ok(new SettingsResponse(domain));
        }).RequireAuthorization(AdminRoles.SuperAdminPolicy);
    }

    public record UpdateSettingsRequest(string? CustomDomain);

    public record SettingsResponse(string? CustomDomain);
}