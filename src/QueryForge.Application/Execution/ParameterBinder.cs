using System.Text.Json;
using System.Text.RegularExpressions;
using QueryForge.Application.ApiOperations;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.Execution;

/// <summary>
/// Turns caller-supplied JSON values into validated strings for the operation's declared parameters. Unknown names
/// are rejected (not ignored), so a caller can never add query keys or path values the administrator did not declare.
/// Error messages never repeat the supplied values.
/// </summary>
public static partial class ParameterBinder
{
    public static IReadOnlyList<ValidationIssue> Bind(
        ApiOperation operation,
        IReadOnlyDictionary<string, JsonElement>? supplied,
        out IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(operation);

        supplied ??= new Dictionary<string, JsonElement>();
        var issues = new List<ValidationIssue>();
        var bound = new Dictionary<string, string>(StringComparer.Ordinal);
        values = bound;

        if (supplied.Count > ApiOperationValidator.MaxParameters)
        {
            issues.Add(new ValidationIssue("parameters", $"At most {ApiOperationValidator.MaxParameters} parameters can be supplied."));
            return issues;
        }

        var declared = operation.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);
        foreach (var name in supplied.Keys.Where(name => !declared.ContainsKey(name)))
        {
            // Only echo names that look like parameter names, so arbitrary caller input is not reflected back.
            issues.Add(SafeNameRegex().IsMatch(name)
                ? new ValidationIssue($"parameters.{name}", "Unknown parameter.")
                : new ValidationIssue("parameters", "An unknown parameter was supplied."));
        }

        foreach (var parameter in operation.Parameters)
        {
            var field = $"parameters.{parameter.Name}";
            if (!supplied.TryGetValue(parameter.Name, out var element))
            {
                if (parameter.IsRequired)
                {
                    issues.Add(new ValidationIssue(field, "Parameter is required."));
                }
                else if (parameter.DefaultValue is not null)
                {
                    bound[parameter.Name] = parameter.DefaultValue;
                }

                continue;
            }

            var text = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };

            if (text is null)
            {
                issues.Add(new ValidationIssue(field, "Value must be a string, number or boolean."));
                continue;
            }

            // A whole path segment of "", "." or ".." would collapse or climb the path once a server decodes it.
            if (parameter.Location == ParameterLocation.Path && text is "" or "." or "..")
            {
                issues.Add(new ValidationIssue(field, "Value is not allowed as a path segment."));
                continue;
            }

            if (!ParameterValueRules.TryValidate(parameter, text, out var error))
            {
                issues.Add(new ValidationIssue(field, $"Value {error}"));
                continue;
            }

            bound[parameter.Name] = text;
        }

        return issues;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeNameRegex();
}
