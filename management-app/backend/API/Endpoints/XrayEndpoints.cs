using System.Security.Claims;
using API.Auth;
using API.Services.Xray;

namespace API.Endpoints;

public static class XrayEndpoints
{
    private sealed class Log { }

    public static void MapXrayEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/xray").RequireAuthorization(AdminRoles.SuperAdminPolicy);

        group.MapGet("/profiles", (XrayRuntime runtime, XrayProfileCatalog catalog) =>
        {
            var profiles = catalog.List()
                .Select(p => new ProfileResponse(
                    p.Name,
                    p.Name == runtime.SelectedProfile,
                    p.IsValid,
                    p.Errors,
                    p.Inbounds.Select(i => new InboundResponse(i.Tag, i.Network, i.Security, i.Port, i.Flow)).ToList()))
                .ToList();
            return Results.Ok(new ProfilesResponse(
                runtime.IsProfileMode ? "profiles" : "legacy",
                runtime.SelectedProfile,
                runtime.IsApplied,
                runtime.LastError,
                profiles));
        });

        group.MapPut("/active-profile", async (
            ActivateProfileRequest req,
            ClaimsPrincipal me,
            XrayRuntime runtime,
            ILogger<Log> logger) =>
        {
            logger.LogInformation("PUT /xray/active-profile — {Actor} activates {Profile}", me.GetUsername(), req.Name);
            return ToResult(await runtime.ActivateAsync(req.Name?.Trim() ?? string.Empty));
        });

        // Re-applies the active profile after its files were edited on disk.
        group.MapPost("/active-profile/reapply", async (ClaimsPrincipal me, XrayRuntime runtime, ILogger<Log> logger) =>
        {
            logger.LogInformation("POST /xray/active-profile/reapply — {Actor} re-applies {Profile}", me.GetUsername(), runtime.SelectedProfile);
            return ToResult(await runtime.ActivateAsync(runtime.SelectedProfile ?? string.Empty));
        });
    }

    private static IResult ToResult(ActivationResult result) => result.Status switch
    {
        ActivationStatus.Activated => Results.Ok(new { active = result.Active }),
        ActivationStatus.NotFound => Results.NotFound(),
        ActivationStatus.Invalid => Results.BadRequest(new { errors = result.Errors }),
        ActivationStatus.LegacyMode or ActivationStatus.Busy => Results.Conflict(new { error = result.Message }),
        ActivationStatus.RolledBack => Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    public record ActivateProfileRequest(string? Name);

    public record InboundResponse(string Tag, string Network, string Security, string? Port, string Flow);

    public record ProfileResponse(string Name, bool Active, bool Valid, IReadOnlyList<string> Errors, IReadOnlyList<InboundResponse> Inbounds);

    public record ProfilesResponse(string Mode, string? Active, bool? Applied, string? LastError, IReadOnlyList<ProfileResponse> Profiles);
}
