using System.Globalization;
using System.Text.RegularExpressions;
using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.ApiOperations;

/// <summary>
/// Checks one parameter value against its declared type and constraints. Used at registration for defaults and
/// allowed values, and at execution for caller values, so both apply exactly the same rules.
/// </summary>
public static class ParameterValueRules
{
    public const int AbsoluteMaxLength = 1024;

    // NonBacktracking runs in linear time, so an administrator-supplied pattern cannot cause catastrophic
    // backtracking; the timeout is a second line of defence.
    public const RegexOptions PatternOptions = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
    public static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Patterns must match the whole value, so they are anchored here rather than trusted to be.</summary>
    public static string Anchor(string pattern) => $"^(?:{pattern})$";

    public static bool TryValidate(OperationParameter parameter, string value, out string? error)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(value);

        error = Check(parameter, value);
        return error is null;
    }

    private static string? Check(OperationParameter parameter, string value)
    {
        var maxLength = parameter.MaxLength ?? AbsoluteMaxLength;
        if (value.Length > maxLength)
        {
            return $"must be {maxLength} characters or fewer.";
        }

        if (value.Any(char.IsControl))
        {
            return "must not contain control characters.";
        }

        switch (parameter.Type)
        {
            case ParameterType.Integer:
                if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                {
                    return "must be an integer.";
                }

                if (OutOfRange(integer, parameter) is { } integerRange)
                {
                    return integerRange;
                }

                break;

            case ParameterType.Number:
                if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    return "must be a number.";
                }

                if (OutOfRange(number, parameter) is { } numberRange)
                {
                    return numberRange;
                }

                break;

            case ParameterType.Boolean:
                if (value is not ("true" or "false"))
                {
                    return "must be true or false.";
                }

                break;
        }

        if (parameter.AllowedValues.Count > 0 && !parameter.AllowedValues.Contains(value, StringComparer.Ordinal))
        {
            return "is not one of the allowed values.";
        }

        if (parameter.Pattern is not null)
        {
            try
            {
                if (!Regex.IsMatch(value, Anchor(parameter.Pattern), PatternOptions, PatternTimeout))
                {
                    return "does not match the required pattern.";
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return "could not be validated against the required pattern.";
            }
        }

        return null;
    }

    private static string? OutOfRange(decimal value, OperationParameter parameter)
    {
        if (parameter.Minimum is { } minimum && value < minimum)
        {
            return $"must be at least {minimum.ToString(CultureInfo.InvariantCulture)}.";
        }

        if (parameter.Maximum is { } maximum && value > maximum)
        {
            return $"must be at most {maximum.ToString(CultureInfo.InvariantCulture)}.";
        }

        return null;
    }
}
