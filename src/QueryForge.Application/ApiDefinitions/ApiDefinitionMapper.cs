using QueryForge.Application.Secrets;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.ApiDefinitions;

internal static class ApiDefinitionMapper
{
    public static ApiDefinitionDto ToDto(this ApiDefinition apiDefinition, ISecretProvider secretProvider)
    {
        return new ApiDefinitionDto(
            apiDefinition.Id,
            apiDefinition.Name,
            apiDefinition.Slug,
            apiDefinition.Description,
            apiDefinition.BaseUrl,
            apiDefinition.Status,
            apiDefinition.AuthenticationType,
            apiDefinition.DocumentationUrl,
            apiDefinition.Tags,
            apiDefinition.CreatedAtUtc,
            apiDefinition.UpdatedAtUtc,
            Convert.ToBase64String(apiDefinition.RowVersion),
            apiDefinition.UpstreamSecretName,
            apiDefinition.ApiKeyHeaderName,
            // Only whether a value exists is reported, so administrators can spot a missing secret.
            apiDefinition.UpstreamSecretName is { } name && secretProvider.TryGetSecret(name, out _));
    }
}
