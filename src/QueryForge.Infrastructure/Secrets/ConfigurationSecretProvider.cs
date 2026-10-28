using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using QueryForge.Application.Secrets;

namespace QueryForge.Infrastructure.Secrets;

/// <summary>
/// Secrets from the <c>Execution:Secrets</c> configuration section (user secrets locally, environment variables in
/// Compose, Key Vault references on Azure). The section is read once at startup, so a changed value takes effect on
/// restart. Values are kept in memory only and never appear in exception messages or logs.
/// </summary>
public sealed partial class ConfigurationSecretProvider : ISecretProvider
{
    public const string SectionName = "Execution:Secrets";

    private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public ConfigurationSecretProvider(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var invalidNames = new List<string>();
        foreach (var entry in configuration.GetSection(SectionName).GetChildren())
        {
            if (!NameRegex().IsMatch(entry.Key))
            {
                // The name is reported, the value never is.
                invalidNames.Add(entry.Key);
                continue;
            }

            if (!string.IsNullOrEmpty(entry.Value))
            {
                _secrets[entry.Key] = entry.Value;
            }
        }

        if (invalidNames.Count > 0)
        {
            throw new InvalidOperationException(
                $"{SectionName} contains invalid secret names ({string.Join(", ", invalidNames)}). Use letters, numbers and hyphens (64 characters or fewer).");
        }
    }

    public bool TryGetSecret(string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        return !string.IsNullOrEmpty(name) && _secrets.TryGetValue(name, out value);
    }

    [GeneratedRegex("^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
