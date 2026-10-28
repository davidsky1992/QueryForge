using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.ApiDefinitions;

public sealed record CreateApiDefinitionRequest(
    string Name,
    string Slug,
    string? Description,
    string BaseUrl,
    ApiDefinitionStatus Status,
    ApiAuthenticationType AuthenticationType,
    string? DocumentationUrl,
    string? Tags,
    string? UpstreamSecretName = null,
    string? ApiKeyHeaderName = null);
