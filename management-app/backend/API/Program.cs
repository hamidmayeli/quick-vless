using System.Text;
using System.Text.Json;
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
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

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
        };
    });
builder.Services.AddAuthorization();

// Xray services
builder.Services.AddSingleton<XrayService>();
builder.Services.AddSingleton<VlessUrlGenerator>();

// Hosted services
builder.Services.AddHostedService<XrayConfigSyncService>();
builder.Services.AddHostedService<MetricsCollectorService>();
builder.Services.AddHostedService<QuotaEnforcerService>();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();
api.MapUsersEndpoints();
api.MapConfigEndpoints();
api.MapUsageEndpoints();

// SPA fallback
app.MapFallbackToFile("index.html");

app.Run();
