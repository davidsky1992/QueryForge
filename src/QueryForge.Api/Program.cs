using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QueryForge.Api.Health;
using QueryForge.Api.Middleware;
using QueryForge.Application;
using QueryForge.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// SQL Server is required to serve requests, so its failure makes the API unhealthy (HTTP 503).
// Redis only backs the optional distributed cache, so its failure is reported as degraded (HTTP 200).
var healthCheckTimeout = TimeSpan.FromSeconds(3);
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlServer", HealthStatus.Unhealthy, timeout: healthCheckTimeout)
    .AddCheck<RedisHealthCheck>("redis", HealthStatus.Degraded, timeout: healthCheckTimeout);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalDevelopment", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "https://localhost:5173",
                "http://127.0.0.1:5173",
                "https://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("LocalDevelopment");
}

app.UseHttpsRedirection();
app.MapControllers();

// Readiness: checks every dependency and reports each one independently.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync
});

// Liveness: confirms the process is responding without touching dependencies.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.Run();
