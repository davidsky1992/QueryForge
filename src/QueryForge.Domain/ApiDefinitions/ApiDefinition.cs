namespace QueryForge.Domain.ApiDefinitions;

public sealed class ApiDefinition
{
    private ApiDefinition()
    {
    }

    private ApiDefinition(
        Guid id,
        string name,
        string slug,
        string? description,
        string baseUrl,
        ApiDefinitionStatus status,
        ApiAuthenticationType authenticationType,
        string? documentationUrl,
        string? tags,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Slug = slug;
        Description = description;
        BaseUrl = baseUrl;
        Status = status;
        AuthenticationType = authenticationType;
        DocumentationUrl = documentationUrl;
        Tags = tags;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string BaseUrl { get; private set; } = string.Empty;
    public ApiDefinitionStatus Status { get; private set; }
    public ApiAuthenticationType AuthenticationType { get; private set; }
    public string? DocumentationUrl { get; private set; }
    public string? Tags { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// Name of the configured secret holding the upstream API key (ApiKey authentication only). The secret value is
    /// never stored here; it is read from configuration (<c>Execution:Secrets:&lt;name&gt;</c>) when executing.
    /// </summary>
    public string? UpstreamSecretName { get; private set; }

    /// <summary>Request header the upstream API key is sent in (ApiKey authentication only).</summary>
    public string? ApiKeyHeaderName { get; private set; }

    public static ApiDefinition Create(
        string name,
        string slug,
        string? description,
        string baseUrl,
        ApiDefinitionStatus status,
        ApiAuthenticationType authenticationType,
        string? documentationUrl,
        string? tags,
        DateTimeOffset createdAtUtc,
        string? upstreamSecretName = null,
        string? apiKeyHeaderName = null)
    {
        return new ApiDefinition(
            Guid.NewGuid(),
            name.Trim(),
            slug.Trim().ToLowerInvariant(),
            NormalizeOptional(description),
            baseUrl.Trim(),
            status,
            authenticationType,
            NormalizeOptional(documentationUrl),
            NormalizeOptional(tags),
            createdAtUtc)
        {
            UpstreamSecretName = NormalizeOptional(upstreamSecretName)?.ToLowerInvariant(),
            ApiKeyHeaderName = NormalizeOptional(apiKeyHeaderName)
        };
    }

    public void Update(
        string name,
        string slug,
        string? description,
        string baseUrl,
        ApiDefinitionStatus status,
        ApiAuthenticationType authenticationType,
        string? documentationUrl,
        string? tags,
        DateTimeOffset updatedAtUtc,
        string? upstreamSecretName = null,
        string? apiKeyHeaderName = null)
    {
        UpstreamSecretName = NormalizeOptional(upstreamSecretName)?.ToLowerInvariant();
        ApiKeyHeaderName = NormalizeOptional(apiKeyHeaderName);
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = NormalizeOptional(description);
        BaseUrl = baseUrl.Trim();
        Status = status;
        AuthenticationType = authenticationType;
        DocumentationUrl = NormalizeOptional(documentationUrl);
        Tags = NormalizeOptional(tags);
        UpdatedAtUtc = updatedAtUtc;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
