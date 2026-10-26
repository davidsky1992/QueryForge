using Microsoft.AspNetCore.Mvc.ApplicationModels;
using QueryForge.Api.Controllers;
using QueryForge.Application.Execution;
using QueryForge.Infrastructure.Execution;

namespace QueryForge.Api.Execution;

public static class ExecutionServiceCollectionExtensions
{
    /// <summary>IHttpClientFactory name of the upstream client.</summary>
    public const string UpstreamClientName = "QueryForge.Upstream";

    /// <summary>
    /// Registers operation execution: options (validated at startup), the destination policy, the guarded connector,
    /// the hardened upstream HttpClient and the execution service. The endpoint itself is only exposed when
    /// <c>Execution:Enabled</c> is true (see <see cref="ExecutionEndpointConvention"/>).
    /// </summary>
    public static IServiceCollection AddOperationExecution(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = ReadOptions(configuration, environment);
        services.AddSingleton(options);
        services.AddSingleton(new ExecutionConcurrencyLimiter(options.MaxConcurrentExecutions));

        services.AddSingleton(new DestinationPolicy(options.AllowedPorts));
        services.AddSingleton<IDnsResolver, SystemDnsResolver>();
        services.AddSingleton<ISocketConnector, SystemSocketConnector>();
        services.AddSingleton(provider => new GuardedConnector(
            provider.GetRequiredService<DestinationPolicy>(),
            provider.GetRequiredService<IDnsResolver>(),
            provider.GetRequiredService<ISocketConnector>()));

        services.AddHttpClient<IUpstreamClient, HttpUpstreamClient>(UpstreamClientName)
            // Each call has its own timeout (linked token); HttpClient's global timeout would blur 504 and 499.
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(provider => UpstreamHandlerFactory.CreatePrimaryHandler(provider.GetRequiredService<GuardedConnector>()))
            .AddHttpMessageHandler(() => new UpstreamHttpVersionHandler())
            // The default IHttpClientFactory loggers write the full request URI, which contains caller-supplied
            // query values. Nothing about an upstream call is logged by the HTTP stack.
            .RemoveAllLoggers();

        services.AddScoped<IExecutionService, ExecutionService>();

        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(mvc => mvc.Conventions.Add(new ExecutionEndpointConvention(options.Enabled)));

        return services;
    }

    private static ExecutionOptions ReadOptions(IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(ExecutionOptions.SectionName);
        var options = section.Get<ExecutionOptions>() ?? new ExecutionOptions();

        // Bind the port list separately: binding into the pre-filled default list would append to it, so a
        // configured list could never remove 80 or 443.
        var ports = section.GetSection(nameof(ExecutionOptions.AllowedPorts)).Get<int[]>();
        options.AllowedPorts = ports is { Length: > 0 } ? [.. ports.Distinct()] : [80, 443];

        var failures = new List<string>();
        if (options.AllowedPorts.Any(port => port is < 1 or > 65535))
        {
            failures.Add("Execution:AllowedPorts must contain ports between 1 and 65535.");
        }

        if (options.MaxConcurrentExecutions < 1)
        {
            failures.Add("Execution:MaxConcurrentExecutions must be at least 1.");
        }

        if (options.AllowHttp && environment.IsProduction())
        {
            failures.Add("Execution:AllowHttp must not be enabled in Production.");
        }

        return failures.Count == 0
            ? options
            : throw new InvalidOperationException("Invalid execution configuration: " + string.Join(" ", failures));
    }
}

/// <summary>Removes the execution controller entirely unless execution is enabled, so its routes do not exist.</summary>
public sealed class ExecutionEndpointConvention(bool enabled) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        if (enabled)
        {
            return;
        }

        foreach (var controller in application.Controllers.Where(controller => controller.ControllerType == typeof(ExecutionController)).ToArray())
        {
            application.Controllers.Remove(controller);
        }
    }
}

/// <summary>
/// Caps upstream calls in flight. Requests over the cap are rejected immediately (503) instead of queueing, so a
/// slow upstream cannot pile up requests and threads.
/// </summary>
public sealed class ExecutionConcurrencyLimiter(int maxConcurrent)
{
    private readonly SemaphoreSlim _slots = new(maxConcurrent, maxConcurrent);

    public bool TryAcquire() => _slots.Wait(0);

    public void Release() => _slots.Release();
}
