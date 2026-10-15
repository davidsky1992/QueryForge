using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Infrastructure.ApiDefinitions;
using QueryForge.Infrastructure.Persistence;

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

        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "QueryForge:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        return services;
    }
}
