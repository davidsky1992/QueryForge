using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Application.ApiOperations;
using QueryForge.Application.Auditing;
using QueryForge.Infrastructure.ApiDefinitions;
using QueryForge.Infrastructure.ApiOperations;
using QueryForge.Infrastructure.Auditing;
using QueryForge.Infrastructure.Persistence;
using StackExchange.Redis;

namespace QueryForge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var sqlServerConnectionString = configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException(
                "Connection string 'SqlServer' is not configured. Set it with 'dotnet user-secrets' for local development " +
                "or the 'ConnectionStrings__SqlServer' environment variable elsewhere.");

        services.AddDbContext<QueryForgeDbContext>(options =>
            options.UseSqlServer(sqlServerConnectionString));

        services.AddScoped<IApiDefinitionRepository, EfApiDefinitionRepository>();
        services.AddScoped<IApiOperationRepository, EfApiOperationRepository>();
        services.AddScoped<IAuditLog, EfAuditLog>();

        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            // One shared multiplexer serves both the distributed cache and the Redis health check.
            // AbortOnConnectFail=false lets the API start while Redis is down and reconnect in the background.
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
                redisOptions.AbortOnConnectFail = false;
                return ConnectionMultiplexer.Connect(redisOptions);
            });

            services.AddStackExchangeRedisCache(options => options.InstanceName = "QueryForge:");
            services.AddOptions<RedisCacheOptions>()
                .Configure<IConnectionMultiplexer>((options, multiplexer) =>
                    options.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer));
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        return services;
    }
}
