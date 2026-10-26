using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.ApiOperations;

public sealed record OperationParameterDto(
    string Name,
    ParameterLocation Location,
    ParameterType Type,
    bool Required,
    int? MaxLength,
    decimal? Minimum,
    decimal? Maximum,
    string? Pattern,
    IReadOnlyList<string> AllowedValues,
    string? Default);

public sealed record ApiOperationDto(
    Guid Id,
    Guid ApiDefinitionId,
    string Key,
    OperationHttpMethod Method,
    string PathTemplate,
    IReadOnlyList<OperationParameterDto> Parameters,
    int TimeoutSeconds,
    int MaxResponseBytes,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);

public static class ApiOperationMapper
{
    public static ApiOperationDto ToDto(this ApiOperation operation)
    {
        return new ApiOperationDto(
            operation.Id,
            operation.ApiDefinitionId,
            operation.Key,
            operation.Method,
            operation.PathTemplate,
            operation.Parameters.Select(ToDto).ToArray(),
            operation.TimeoutSeconds,
            operation.MaxResponseBytes,
            operation.IsEnabled,
            operation.CreatedAtUtc,
            operation.UpdatedAtUtc,
            Convert.ToBase64String(operation.RowVersion));
    }

    public static OperationParameter ToDomain(this OperationParameterRequest request)
    {
        return new OperationParameter(
            request.Name,
            request.Location,
            request.Type,
            request.Required,
            request.MaxLength,
            request.Minimum,
            request.Maximum,
            request.Pattern,
            request.AllowedValues,
            request.Default);
    }

    private static OperationParameterDto ToDto(OperationParameter parameter)
    {
        return new OperationParameterDto(
            parameter.Name,
            parameter.Location,
            parameter.Type,
            parameter.IsRequired,
            parameter.MaxLength,
            parameter.Minimum,
            parameter.Maximum,
            parameter.Pattern,
            parameter.AllowedValues,
            parameter.DefaultValue);
    }
}
