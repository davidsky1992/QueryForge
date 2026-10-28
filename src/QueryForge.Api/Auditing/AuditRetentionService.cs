using QueryForge.Application.Auditing;

namespace QueryForge.Api.Auditing;

/// <summary>
/// Deletes audit events older than <see cref="AuditRetentionOptions.RetentionDays"/>, once at startup and then on every
/// <see cref="AuditRetentionOptions.RetentionInterval"/>. Each API instance runs it; the delete is idempotent.
/// </summary>
public sealed class AuditRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AuditRetentionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuditRetentionService> _logger;

    public AuditRetentionService(IServiceScopeFactory scopeFactory, AuditRetentionOptions options, TimeProvider timeProvider, ILogger<AuditRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs one purge and returns the number of deleted events.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow().AddDays(-_options.RetentionDays);
        using var scope = _scopeFactory.CreateScope();
        var deleted = await scope.ServiceProvider.GetRequiredService<IAuditEventRepository>().DeleteOlderThanAsync(cutoff, cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation("Deleted {Count} audit events older than {Cutoff:O} ({RetentionDays}-day retention).", deleted, cutoff, _options.RetentionDays);
        }

        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.RetentionInterval, _timeProvider);
        do
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed purge (for example the database is briefly unavailable) is retried on the next tick.
                _logger.LogWarning(exception, "Audit retention purge failed; it will be retried.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public static class AuditRetentionServiceCollectionExtensions
{
    public static IServiceCollection AddAuditRetention(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AuditRetentionOptions.SectionName).Get<AuditRetentionOptions>() ?? new AuditRetentionOptions();

        if (options.RetentionDays is < 1 or > 3650)
        {
            throw new InvalidOperationException("Audit:RetentionDays must be between 1 and 3650.");
        }

        if (options.RetentionInterval < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException("Audit:RetentionInterval must be at least one second.");
        }

        services.AddSingleton(options);
        if (options.RetentionEnabled)
        {
            services.AddHostedService<AuditRetentionService>();
        }

        return services;
    }
}
