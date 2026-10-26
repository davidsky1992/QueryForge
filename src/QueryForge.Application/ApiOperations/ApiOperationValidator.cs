using System.Text.RegularExpressions;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.ApiOperations;

/// <summary>
/// Registration-time validation of operations. The path template may contain only unreserved literal characters and
/// whole-segment placeholders, so no template can introduce a scheme, host, query, fragment, encoded character or
/// dot segment. Destination checks at connection time remain authoritative; this only rejects obvious mistakes early.
/// </summary>
public static partial class ApiOperationValidator
{
    public const int KeyMaxLength = 64;
    public const int PathTemplateMaxLength = 512;
    public const int MinTimeoutSeconds = 1;
    public const int MaxTimeoutSeconds = 30;
    public const int DefaultTimeoutSeconds = 10;
    public const int MaxResponseBytesLimit = 1024 * 1024;
    public const int DefaultMaxResponseBytes = 256 * 1024;
    public const int MaxParameters = 20;
    public const int PatternMaxLength = 256;
    public const int MaxAllowedValues = 50;
    public const int AllowedValueMaxLength = 256;

    public static IReadOnlyList<ValidationIssue> Validate(CreateApiOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ValidateCore(request.Key, request.Method, request.PathTemplate, request.Parameters, request.TimeoutSeconds, request.MaxResponseBytes);
    }

    public static IReadOnlyList<ValidationIssue> Validate(UpdateApiOperationRequest request, out byte[] parsedRowVersion)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = ValidateCore(request.Key, request.Method, request.PathTemplate, request.Parameters, request.TimeoutSeconds, request.MaxResponseBytes);
        return [.. issues, .. ApiDefinitionValidator.ValidateRowVersion(request.RowVersion, out parsedRowVersion)];
    }

    public static IReadOnlyList<ValidationIssue> ValidateRowVersion(string? rowVersion, out byte[] parsedRowVersion) =>
        ApiDefinitionValidator.ValidateRowVersion(rowVersion, out parsedRowVersion);

    /// <summary>Parses a path template into its placeholder names, or returns an error message.</summary>
    public static string? TryParsePathTemplate(string? template, out IReadOnlyList<string> placeholders)
    {
        placeholders = [];

        if (string.IsNullOrWhiteSpace(template))
        {
            return "pathTemplate is required.";
        }

        if (template.Length > PathTemplateMaxLength)
        {
            return $"pathTemplate must be {PathTemplateMaxLength} characters or fewer.";
        }

        if (template[0] != '/')
        {
            return "pathTemplate must be a relative path starting with '/'.";
        }

        var names = new List<string>();
        var segments = template[1..].Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];

            // An empty segment is only allowed as a trailing slash (or the root template "/").
            if (segment.Length == 0)
            {
                if (index == segments.Length - 1)
                {
                    continue;
                }

                return "pathTemplate must not contain empty segments ('//').";
            }

            var placeholder = PlaceholderRegex().Match(segment);
            if (placeholder.Success)
            {
                var name = placeholder.Groups["name"].Value;
                if (names.Contains(name, StringComparer.Ordinal))
                {
                    return $"pathTemplate uses the placeholder '{name}' more than once.";
                }

                names.Add(name);
                continue;
            }

            if (segment is "." or ".." || !LiteralSegmentRegex().IsMatch(segment))
            {
                return "pathTemplate segments may contain only letters, digits, '-', '.', '_' and '~', or a whole-segment {placeholder}.";
            }
        }

        placeholders = names;
        return null;
    }

    private static List<ValidationIssue> ValidateCore(
        string? key,
        OperationHttpMethod method,
        string? pathTemplate,
        IReadOnlyList<OperationParameterRequest>? parameters,
        int timeoutSeconds,
        int maxResponseBytes)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(key))
        {
            issues.Add(new ValidationIssue(nameof(key), "key is required."));
        }
        else if (key.Length > KeyMaxLength || !KeyRegex().IsMatch(key))
        {
            issues.Add(new ValidationIssue(nameof(key), $"key must be {KeyMaxLength} characters or fewer and contain only lowercase letters, numbers, and hyphens."));
        }

        if (!Enum.IsDefined(method))
        {
            issues.Add(new ValidationIssue(nameof(method), "method is not supported. Only GET is supported."));
        }

        if (timeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
        {
            issues.Add(new ValidationIssue(nameof(timeoutSeconds), $"timeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}."));
        }

        if (maxResponseBytes is < 1 or > MaxResponseBytesLimit)
        {
            issues.Add(new ValidationIssue(nameof(maxResponseBytes), $"maxResponseBytes must be between 1 and {MaxResponseBytesLimit}."));
        }

        var templateError = TryParsePathTemplate(pathTemplate, out var placeholders);
        if (templateError is not null)
        {
            issues.Add(new ValidationIssue(nameof(pathTemplate), templateError));
        }

        parameters ??= [];
        if (parameters.Count > MaxParameters)
        {
            issues.Add(new ValidationIssue(nameof(parameters), $"An operation can declare at most {MaxParameters} parameters."));
            return issues;
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            var field = $"parameters[{index}]";
            if (parameter is null)
            {
                issues.Add(new ValidationIssue(field, "Parameter must not be null."));
                continue;
            }

            ValidateParameter(parameter, field, issues);

            if (!string.IsNullOrWhiteSpace(parameter.Name) && !seenNames.Add(parameter.Name.Trim()))
            {
                issues.Add(new ValidationIssue($"{field}.name", $"Parameter '{parameter.Name.Trim()}' is declared more than once."));
            }
        }

        // Path placeholders and path parameters must correspond exactly.
        if (templateError is null)
        {
            var pathParameters = parameters
                .Where(parameter => parameter is not null && parameter.Location == ParameterLocation.Path && !string.IsNullOrWhiteSpace(parameter.Name))
                .Select(parameter => parameter.Name.Trim())
                .ToHashSet(StringComparer.Ordinal);

            foreach (var placeholder in placeholders.Where(placeholder => !pathParameters.Contains(placeholder)))
            {
                issues.Add(new ValidationIssue(nameof(pathTemplate), $"Placeholder '{placeholder}' has no matching path parameter."));
            }

            foreach (var name in pathParameters.Where(name => !placeholders.Contains(name, StringComparer.Ordinal)))
            {
                issues.Add(new ValidationIssue(nameof(parameters), $"Path parameter '{name}' does not appear in pathTemplate."));
            }
        }

        return issues;
    }

    private static void ValidateParameter(OperationParameterRequest parameter, string field, List<ValidationIssue> issues)
    {
        var name = parameter.Name?.Trim();
        if (string.IsNullOrEmpty(name) || !ParameterNameRegex().IsMatch(name))
        {
            issues.Add(new ValidationIssue($"{field}.name", "name must start with a letter and contain only letters, digits, '_', '.' or '-' (64 characters or fewer)."));
        }

        if (!Enum.IsDefined(parameter.Location))
        {
            issues.Add(new ValidationIssue($"{field}.location", "location must be path or query."));
        }

        if (!Enum.IsDefined(parameter.Type))
        {
            issues.Add(new ValidationIssue($"{field}.type", "type must be string, integer, number or boolean."));
            return;
        }

        if (parameter.Location == ParameterLocation.Path)
        {
            if (!parameter.Required)
            {
                issues.Add(new ValidationIssue($"{field}.required", "Path parameters must be required."));
            }
        }

        if (parameter.Required && parameter.Default is not null)
        {
            issues.Add(new ValidationIssue($"{field}.default", "A required parameter cannot have a default value."));
        }

        var isString = parameter.Type == ParameterType.String;
        var isNumeric = parameter.Type is ParameterType.Integer or ParameterType.Number;

        if (parameter.MaxLength is { } maxLength && (!isString || maxLength < 1 || maxLength > ParameterValueRules.AbsoluteMaxLength))
        {
            issues.Add(new ValidationIssue($"{field}.maxLength", $"maxLength applies to string parameters only and must be between 1 and {ParameterValueRules.AbsoluteMaxLength}."));
        }

        if ((parameter.Minimum is not null || parameter.Maximum is not null) && !isNumeric)
        {
            issues.Add(new ValidationIssue($"{field}.minimum", "minimum and maximum apply to integer and number parameters only."));
        }
        else if (parameter.Minimum > parameter.Maximum)
        {
            issues.Add(new ValidationIssue($"{field}.minimum", "minimum must not be greater than maximum."));
        }
        else if (parameter.Type == ParameterType.Integer &&
            (parameter.Minimum is { } min && min != decimal.Truncate(min) || parameter.Maximum is { } max && max != decimal.Truncate(max)))
        {
            issues.Add(new ValidationIssue($"{field}.minimum", "minimum and maximum of an integer parameter must be whole numbers."));
        }

        var patternValid = true;
        if (parameter.Pattern is not null)
        {
            patternValid = ValidatePattern(parameter.Pattern, isString, field, issues);
        }

        var allowedValues = parameter.AllowedValues ?? [];
        if (allowedValues.Count > MaxAllowedValues)
        {
            issues.Add(new ValidationIssue($"{field}.allowedValues", $"At most {MaxAllowedValues} allowed values can be declared."));
            return;
        }

        if (allowedValues.Any(value => string.IsNullOrEmpty(value) || value.Length > AllowedValueMaxLength))
        {
            issues.Add(new ValidationIssue($"{field}.allowedValues", $"Allowed values must be non-empty and {AllowedValueMaxLength} characters or fewer."));
            return;
        }

        if (allowedValues.Distinct(StringComparer.Ordinal).Count() != allowedValues.Count)
        {
            issues.Add(new ValidationIssue($"{field}.allowedValues", "Allowed values must be unique."));
            return;
        }

        // Defaults and allowed values must themselves satisfy the declared type and constraints. Only checked once
        // the constraints are known to be well-formed, so a single mistake produces a single, clear error.
        if (!patternValid || issues.Any(issue => issue.Field.StartsWith(field, StringComparison.Ordinal)))
        {
            return;
        }

        var domainParameter = parameter.ToDomain();
        foreach (var value in allowedValues)
        {
            if (!ParameterValueRules.TryValidate(domainParameter, value, out var error))
            {
                issues.Add(new ValidationIssue($"{field}.allowedValues", $"Allowed value '{value}' {error}"));
            }
        }

        if (parameter.Default is { } defaultValue && !ParameterValueRules.TryValidate(domainParameter, defaultValue, out var defaultError))
        {
            issues.Add(new ValidationIssue($"{field}.default", $"default {defaultError}"));
        }
    }

    private static bool ValidatePattern(string pattern, bool isString, string field, List<ValidationIssue> issues)
    {
        if (!isString)
        {
            issues.Add(new ValidationIssue($"{field}.pattern", "pattern applies to string parameters only."));
            return false;
        }

        if (pattern.Length == 0 || pattern.Length > PatternMaxLength)
        {
            issues.Add(new ValidationIssue($"{field}.pattern", $"pattern must be between 1 and {PatternMaxLength} characters."));
            return false;
        }

        try
        {
            _ = new Regex(ParameterValueRules.Anchor(pattern), ParameterValueRules.PatternOptions, ParameterValueRules.PatternTimeout);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            // NotSupportedException: constructs such as backreferences or lookarounds that the linear-time engine
            // does not support.
            issues.Add(new ValidationIssue($"{field}.pattern", "pattern is not a supported regular expression (backreferences and lookarounds are not allowed)."));
            return false;
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNameRegex();

    [GeneratedRegex("^\\{(?<name>[A-Za-z][A-Za-z0-9_.-]{0,63})\\}$", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex("^[A-Za-z0-9._~-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex LiteralSegmentRegex();
}
