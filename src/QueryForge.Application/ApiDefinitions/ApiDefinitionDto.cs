using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.ApiDefinitions;

/// <summary>
/// An API definition as returned by the API. For ApiKey authentication only the secret's <em>name</em> and whether a
/// value is configured are exposed — never the value.
/// </summary>
public sealed record ApiDefinitionDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string BaseUrl,
    ApiDefinitionStatus Status,
    ApiAuthenticationType AuthenticationType,
    string? DocumentationUrl,
    string? Tags,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion,
    string? UpstreamSecretName = null,
    string? ApiKeyHeaderName = null,
    bool SecretConfigured = false);
