using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace QueryForge.Api.Security;

public sealed record ApiKeyIdentity(string Name, IReadOnlyList<string> Roles);

/// <summary>
/// Matches presented API keys against configured SHA-256 hashes.
/// </summary>
public sealed class ApiKeyStore
{
    // Generated keys are 43 characters; anything far longer is rejected before hashing.
    public const int MaxKeyLength = 256;

    private readonly IReadOnlyList<StoredKey> _keys;

    public ApiKeyStore(IOptions<SecurityOptions> options)
    {
        _keys = options.Value.ApiKeys
            .Where(key => key.IsConfigured)
            .Select(key => new StoredKey(
                Convert.FromHexString(key.Hash.Trim()),
                new ApiKeyIdentity(key.Name.Trim(), key.ParseRoles())))
            .ToArray();
    }

    public int Count => _keys.Count;

    public ApiKeyIdentity? Find(string? presentedKey)
    {
        if (string.IsNullOrEmpty(presentedKey) || presentedKey.Length > MaxKeyLength)
        {
            return null;
        }

        Span<byte> presentedHash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey), presentedHash);

        // Every configured hash is compared in constant time and the loop never exits early, so response timing
        // does not reveal how many keys exist or which one was closest. Comparing hashes (not raw keys) also means
        // timing could only ever leak information about the hash, not the key.
        ApiKeyIdentity? match = null;
        foreach (var key in _keys)
        {
            if (CryptographicOperations.FixedTimeEquals(presentedHash, key.Hash))
            {
                match = key.Identity;
            }
        }

        return match;
    }

    /// <summary>Hex SHA-256 hash of the UTF-8 key, in the format expected by configuration.</summary>
    public static string ComputeHash(string key)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private sealed record StoredKey(byte[] Hash, ApiKeyIdentity Identity);
}
