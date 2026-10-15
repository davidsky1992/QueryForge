using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.ApiDefinitions;

public sealed record UpdateApiDefinitionRequest(
    string Name,
    string Slug,
    string? Description,
    string BaseUrl,
    ApiDefinitionStatus Status,
    ApiAuthenticationType AuthenticationType,
    string? DocumentationUrl,
    string? Tags);
