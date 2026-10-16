using Microsoft.Extensions.Options;

namespace QueryForge.Api.Security;

/// <summary>
/// Validates API key configuration at startup. Messages identify the offending entry by index or name and never
/// include hash values.
/// </summary>
public sealed class SecurityOptionsValidator : IValidateOptions<SecurityOptions>
{
    private readonly IHostEnvironment _environment;

    public SecurityOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        var failures = new List<string>();
        var configured = new List<ApiKeyOptions>();

        for (var index = 0; index < options.ApiKeys.Count; index++)
        {
            var key = options.ApiKeys[index];
            if (!key.IsConfigured)
            {
                continue;
            }

            configured.Add(key);
            var path = $"{SecurityOptions.SectionName}:ApiKeys:{index}";

            if (string.IsNullOrWhiteSpace(key.Name))
            {
                failures.Add($"{path}:Name is required.");
            }

            if (!IsSha256Hex(key.Hash.Trim()))
            {
                failures.Add($"{path}:Hash must be a 64-character hexadecimal SHA-256 hash.");
            }

            var roles = key.ParseRoles();
            if (roles.Count == 0)
            {
                failures.Add($"{path}:Roles must contain at least one role ({string.Join(", ", ApiKeyRoles.All)}).");
            }

            foreach (var role in roles.Where(role => !ApiKeyRoles.All.Contains(role)))
            {
                failures.Add($"{path}:Roles contains unknown role '{role}'.");
            }
        }

        foreach (var duplicate in configured
                     .GroupBy(key => key.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Key.Length > 0 && group.Count() > 1))
        {
            failures.Add($"API key name '{duplicate.Key}' is used more than once.");
        }

        if (configured
            .GroupBy(key => key.Hash.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            failures.Add("The same API key hash is configured more than once.");
        }

        // Development may run without keys (protected endpoints then return 401); every other environment must
        // be configured explicitly so a deployment can never silently run without working authentication.
        if (configured.Count == 0 && !_environment.IsDevelopment())
        {
            failures.Add(
                $"At least one API key must be configured in {SecurityOptions.SectionName}:ApiKeys " +
                $"outside the Development environment (current: {_environment.EnvironmentName}).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSha256Hex(string value)
    {
        return value.Length == 64 && value.All(Uri.IsHexDigit);
    }
}
