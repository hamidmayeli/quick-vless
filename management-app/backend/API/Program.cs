using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using API.Auth;
using API.Endpoints;
using API.Services;
using API.Storage;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Use snake_case for all JSON request/response serialization
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

// OpenAPI / Scalar
builder.Services.AddOpenApi();

// Storage
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<AdminRepository>();
builder.Services.AddSingleton<UsageRepository>();

// Auth
builder.Services.AddSingleton<TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        var secret = builder.Configuration["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret required");
        var issuer = builder.Configuration["Jwt:Issuer"] ?? "easy-xray";
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = issuer,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };
        o.Events = new JwtBearerEvents { OnTokenValidated = AdminTokenValidator.ValidateAsync };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AdminRoles.SuperAdminPolicy, p => p.RequireRole(AdminRoles.SuperAdmin));

// Xray services
builder.Services.AddSingleton<XrayService>();
builder.Services.AddSingleton<XrayConfigFileService>();
builder.Services.AddSingleton<VlessUrlGenerator>();

// Hosted services
builder.Services.AddHostedService<XrayConfigSyncService>();
builder.Services.AddHostedService<MetricsCollectorService>();
builder.Services.AddHostedService<QuotaEnforcerService>();

var app = builder.Build();

// admins.json written before roles existed has no super admin — promote its first admin.
var promoted = await app.Services.GetRequiredService<AdminRepository>().EnsureSuperAdminAsync();
if (promoted is not null)
    app.Logger.LogInformation("Promoted existing admin {Username} to super admin", promoted);

app.MapOpenApi();
app.MapScalarApiReference();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();
api.MapAdminsEndpoints();
api.MapUsersEndpoints();
api.MapConfigEndpoints();
api.MapUsageEndpoints();

// SPA fallback
app.MapFallbackToFile("index.html");

app.Run();
