using Microsoft.Extensions.DependencyInjection;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Application.ApiOperations;
using QueryForge.Application.Auditing;

namespace QueryForge.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IApiDefinitionService, ApiDefinitionService>();
        services.AddScoped<IApiOperationService, ApiOperationService>();
        services.AddScoped<IAuditEventService, AuditEventService>();

        return services;
    }
}
