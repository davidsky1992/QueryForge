using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QueryForge.Api.Security;

namespace QueryForge.Api.Tests;

public sealed class ApiKeyStoreTests
{
    private const string AdminKey = "admin-key-for-unit-tests-0123456789abcdef";
    private const string ReaderKey = "reader-key-for-unit-tests-0123456789abcdef";

    [Fact]
    public void Find_WithConfiguredKey_ReturnsItsIdentityAndRoles()
    {
        var store = CreateStore();

        var identity = store.Find(AdminKey);

        Assert.NotNull(identity);
        Assert.Equal("ops-admin", identity.Name);
        Assert.Equal([ApiKeyRoles.Admin], identity.Roles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-configured-key")]
    [InlineData("admin-key-for-unit-tests-0123456789abcdeF")] // one character differs
    public void Find_WithUnknownKey_ReturnsNull(string? presentedKey)
    {
        Assert.Null(CreateStore().Find(presentedKey));
    }

    [Fact]
    public void Find_WithOverlongKey_ReturnsNullWithoutMatching()
    {
        Assert.Null(CreateStore().Find(new string('x', ApiKeyStore.MaxKeyLength + 1)));
    }

    [Fact]
    public void Find_AcceptsUppercaseHexHashesAndIgnoresEmptySlots()
    {
        var store = CreateStore(
            new ApiKeyOptions { Name = "upper", Hash = ApiKeyStore.ComputeHash(ReaderKey).ToUpperInvariant(), Roles = "reader" },
            new ApiKeyOptions { Name = "unset-compose-slot", Hash = "", Roles = "admin" });

        Assert.Equal(1, store.Count);
        Assert.Equal("upper", store.Find(ReaderKey)?.Name);
    }

    [Fact]
    public void ComputeHash_ReturnsLowercaseSha256Hex()
    {
        // SHA-256("abc"), a published test vector.
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ApiKeyStore.ComputeHash("abc"));
    }

    private static ApiKeyStore CreateStore(params ApiKeyOptions[] keys)
    {
        if (keys.Length == 0)
        {
            keys =
            [
                new ApiKeyOptions { Name = "ops-admin", Hash = ApiKeyStore.ComputeHash(AdminKey), Roles = "admin" },
                new ApiKeyOptions { Name = "dashboard", Hash = ApiKeyStore.ComputeHash(ReaderKey), Roles = "reader" }
            ];
        }

        return new ApiKeyStore(Options.Create(new SecurityOptions { ApiKeys = [.. keys] }));
    }
}

public sealed class SecurityOptionsValidatorTests
{
    private static readonly string ValidHash = ApiKeyStore.ComputeHash("some-key");

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void NoKeys_OutsideDevelopment_FailsValidation(string environment)
    {
        var result = Validate(environment);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("At least one API key"));
    }

    [Fact]
    public void NoKeys_InDevelopment_IsAllowed()
    {
        // Unset Compose variables produce empty slots; they are ignored rather than rejected.
        var result = Validate("Development", new ApiKeyOptions { Name = "local-admin", Hash = "", Roles = "admin" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidKey_InProduction_Succeeds()
    {
        Assert.True(Validate("Production", new ApiKeyOptions { Name = "prod-admin", Hash = ValidHash, Roles = "admin, reader" }).Succeeded);
    }

    [Theory]
    [InlineData("executor")]
    [InlineData("executor, admin")]
    public void ExecutorRole_IsAccepted(string roles)
    {
        Assert.True(Validate("Production", new ApiKeyOptions { Name = "runner", Hash = ValidHash, Roles = roles }).Succeeded);
    }

    [Theory]
    [InlineData("", "admin", "Name is required")]
    [InlineData("key", "", "at least one role")]
    [InlineData("key", "superuser", "unknown role 'superuser'")]
    public void InvalidEntry_FailsValidation(string name, string roles, string expectedMessage)
    {
        var result = Validate("Development", new ApiKeyOptions { Name = name, Hash = ValidHash, Roles = roles });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains(expectedMessage));
    }

    [Theory]
    [InlineData("not-hex")]
    [InlineData("abc123")]
    [InlineData("plain-text-api-key-that-someone-pasted-by-mistake-into-the-hash")]
    public void NonSha256Hash_FailsValidationWithoutEchoingTheValue(string hash)
    {
        var result = Validate("Production", new ApiKeyOptions { Name = "key", Hash = hash, Roles = "admin" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("64-character hexadecimal"));
        Assert.DoesNotContain(result.Failures!, failure => failure.Contains(hash));
    }

    [Fact]
    public void DuplicateNamesOrHashes_FailValidation()
    {
        var result = Validate(
            "Production",
            new ApiKeyOptions { Name = "same", Hash = ValidHash, Roles = "admin" },
            new ApiKeyOptions { Name = "same", Hash = ValidHash, Roles = "reader" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("used more than once"));
        Assert.Contains(result.Failures!, failure => failure.Contains("hash is configured more than once"));
    }

    private static ValidateOptionsResult Validate(string environment, params ApiKeyOptions[] keys)
    {
        var validator = new SecurityOptionsValidator(new TestHostEnvironment(environment));
        return validator.Validate(null, new SecurityOptions { ApiKeys = [.. keys] });
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "QueryForge.Api.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
