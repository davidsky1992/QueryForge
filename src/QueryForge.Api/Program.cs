using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using QueryForge.Api.Health;
using QueryForge.Api.Http;
using QueryForge.Api.Middleware;
using QueryForge.Api.RateLimiting;
using QueryForge.Api.Security;
using QueryForge.Application;
using QueryForge.Application.Auditing;
using QueryForge.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddApiKeySecurity(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddConfiguredCors(builder.Configuration, builder.Environment);
builder.Services.AddTrustedForwardedHeaders(builder.Configuration);

// SQL Server is required to serve requests, so its failure makes the API unhealthy (HTTP 503).
// Redis only backs the optional distributed cache, so its failure is reported as degraded (HTTP 200).
var healthCheckTimeout = TimeSpan.FromSeconds(3);
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlServer", HealthStatus.Unhealthy, timeout: healthCheckTimeout)
    .AddCheck<RedisHealthCheck>("redis", HealthStatus.Degraded, timeout: healthCheckTimeout);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Lets Swagger UI (Development only) send the X-Api-Key header via "Authorize".
    options.AddSecurityDefinition(ApiKeyAuthenticationDefaults.Scheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ApiKeyAuthenticationDefaults.HeaderName,
        Description = "API key issued with scripts/New-QueryForgeApiKey.ps1."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ApiKeyAuthenticationDefaults.Scheme }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Must run first so the client IP used for rate limiting comes from a trusted proxy only.
app.UseTrustedForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseConfiguredCors();

// Authentication runs before rate limiting so authenticated clients are limited per API key,
// and before authorization so unauthenticated requests are limited before they are rejected.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

// Public endpoints are allowlisted explicitly; everything else falls back to requiring an API key.

// Liveness: the process responds. No dependencies are checked and nothing internal is revealed.
app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = HealthResponseWriter.WriteStatusOnlyAsync
    })
    .AllowAnonymous()
    .DisableRateLimiting();

// Readiness: runs every check but returns only the overall status (200 Healthy/Degraded, 503 Unhealthy).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        ResponseWriter = HealthResponseWriter.WriteStatusOnlyAsync
    })
    .AllowAnonymous()
    .RequireRateLimiting(RateLimitPolicies.Health);

// Detailed per-dependency report for operators with an admin key.
app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = HealthResponseWriter.WriteAsync
    })
    .RequireAuthorization(AuthorizationPolicies.ViewHealthDetails)
    .RequireRateLimiting(RateLimitPolicies.Health);

app.Run();

// Exposes the top-level-statement entry point to WebApplicationFactory<Program> in integration tests.
public partial class Program;
