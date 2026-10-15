using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using QueryForge.Api.Middleware;
using QueryForge.Application;
using QueryForge.Infrastructure;
using QueryForge.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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

app.MapGet("/health", async (
    QueryForgeDbContext dbContext,
    IDistributedCache cache,
    CancellationToken cancellationToken) =>
{
    var databaseReady = await dbContext.Database.CanConnectAsync(cancellationToken);
    await cache.SetStringAsync("health", "ok", cancellationToken);

    return Results.Ok(new
    {
        status = databaseReady ? "Healthy" : "Degraded",
        database = databaseReady ? "Reachable" : "Unreachable",
        cache = "Reachable"
    });
})
.WithName("Health")
.WithOpenApi();

app.Run();
