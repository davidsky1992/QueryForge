namespace QueryForge.Domain.ApiOperations;

/// <summary>
/// A caller-supplied value an operation accepts. Only declared parameters can reach the upstream, and only in the
/// declared location (path segment or query string). Constraints are validated when the operation is registered.
/// </summary>
public sealed class OperationParameter
{
    private OperationParameter()
    {
    }

    public OperationParameter(
        string name,
        ParameterLocation location,
        ParameterType type,
        bool isRequired,
        int? maxLength,
        decimal? minimum,
        decimal? maximum,
        string? pattern,
        IReadOnlyList<string>? allowedValues,
        string? defaultValue)
    {
        Name = name.Trim();
        Location = location;
        Type = type;
        IsRequired = isRequired;
        MaxLength = maxLength;
        Minimum = minimum;
        Maximum = maximum;
        Pattern = string.IsNullOrEmpty(pattern) ? null : pattern;
        AllowedValues = allowedValues is { Count: > 0 } ? [.. allowedValues] : [];
        DefaultValue = defaultValue;
    }

    public string Name { get; private set; } = string.Empty;
    public ParameterLocation Location { get; private set; }
    public ParameterType Type { get; private set; }
    public bool IsRequired { get; private set; }
    public int? MaxLength { get; private set; }
    public decimal? Minimum { get; private set; }
    public decimal? Maximum { get; private set; }
    public string? Pattern { get; private set; }
    public IReadOnlyList<string> AllowedValues { get; private set; } = [];
    public string? DefaultValue { get; private set; }
}
