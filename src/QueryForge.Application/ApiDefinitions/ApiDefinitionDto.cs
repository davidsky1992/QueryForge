using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.ApiDefinitions;

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
    string RowVersion);
