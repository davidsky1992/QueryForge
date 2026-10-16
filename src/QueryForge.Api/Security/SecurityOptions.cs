namespace QueryForge.Api.Security;

/// <summary>
/// Bound from the "Security" configuration section. API keys are stored only as SHA-256 hashes.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public List<ApiKeyOptions> ApiKeys { get; set; } = [];
}

public sealed class ApiKeyOptions
{
    /// <summary>Identifies the key in logs and rate-limit partitions; never the key itself.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Lowercase or uppercase hex SHA-256 hash of the UTF-8 key. An empty hash marks an unused slot
    /// (for example an unset Docker Compose variable) and is ignored.
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>Comma-separated roles: "reader" and/or "admin".</summary>
    public string Roles { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Hash);

    public IReadOnlyList<string> ParseRoles()
    {
        return Roles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(role => role.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
