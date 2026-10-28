using System.Diagnostics.CodeAnalysis;

namespace QueryForge.Application.Secrets;

/// <summary>
/// Supplies secret values by name (for example upstream API keys). Values come from configuration or a secret store,
/// never from the database, and must never be logged, audited or returned by any endpoint.
/// </summary>
public interface ISecretProvider
{
    bool TryGetSecret(string name, [NotNullWhen(true)] out string? value);
}
