using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.ApiOperations;

public sealed record OperationParameterRequest(
    string Name,
    ParameterLocation Location,
    ParameterType Type,
    bool Required,
    int? MaxLength = null,
    decimal? Minimum = null,
    decimal? Maximum = null,
    string? Pattern = null,
    IReadOnlyList<string>? AllowedValues = null,
    string? Default = null);

public sealed record CreateApiOperationRequest(
    string Key,
    OperationHttpMethod Method,
    string PathTemplate,
    IReadOnlyList<OperationParameterRequest>? Parameters = null,
    int TimeoutSeconds = ApiOperationValidator.DefaultTimeoutSeconds,
    int MaxResponseBytes = ApiOperationValidator.DefaultMaxResponseBytes,
    bool Enabled = true);

public sealed record UpdateApiOperationRequest(
    string Key,
    OperationHttpMethod Method,
    string PathTemplate,
    IReadOnlyList<OperationParameterRequest>? Parameters,
    int TimeoutSeconds,
    int MaxResponseBytes,
    bool Enabled,
    string? RowVersion);
