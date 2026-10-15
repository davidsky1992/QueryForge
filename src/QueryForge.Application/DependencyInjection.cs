using Microsoft.Extensions.DependencyInjection;
using QueryForge.Application.ApiDefinitions;

namespace QueryForge.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IApiDefinitionService, ApiDefinitionService>();

        return services;
    }
}
