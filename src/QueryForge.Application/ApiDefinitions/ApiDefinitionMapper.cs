using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.ApiDefinitions;

internal static class ApiDefinitionMapper
{
    public static ApiDefinitionDto ToDto(this ApiDefinition apiDefinition)
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
            Convert.ToBase64String(apiDefinition.RowVersion));
    }
}
