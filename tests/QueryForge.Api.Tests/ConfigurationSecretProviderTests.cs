using Microsoft.Extensions.Configuration;
using QueryForge.Infrastructure.Secrets;

namespace QueryForge.Api.Tests;

public sealed class ConfigurationSecretProviderTests
{
    private const string SecretValue = "SECRET-VALUE-a91f";

    [Fact]
    public void ConfiguredSecret_IsReturnedByName_CaseInsensitively()
    {
        var provider = Create(("weather-api", SecretValue));

        Assert.True(provider.TryGetSecret("weather-api", out var value));
        Assert.Equal(SecretValue, value);
        Assert.True(provider.TryGetSecret("Weather-API", out _));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("")]
    public void UnknownOrEmptyName_IsNotFound(string name)
    {
        Assert.False(Create(("weather-api", SecretValue)).TryGetSecret(name, out var value));
        Assert.Null(value);
    }

    [Fact]
    public void EmptyValue_CountsAsNotConfigured()
    {
        Assert.False(Create(("weather-api", "")).TryGetSecret("weather-api", out _));
    }

    [Fact]
    public void InvalidNames_StopStartup_WithoutRevealingValues()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Create(("bad_name", SecretValue), ("good-name", SecretValue)));

        Assert.Contains("bad_name", exception.Message);
        Assert.DoesNotContain(SecretValue, exception.Message);
    }

    [Theory]
    [InlineData("key\r\nX-Injected: 1")]
    [InlineData("key\n")]
    [InlineData("key\0")]
    public void ValuesWithControlCharacters_StopStartup_WithoutRevealingThem(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Create(("weather-api", value)));

        Assert.Contains("weather-api", exception.Message);
        Assert.DoesNotContain("key", exception.Message.Replace("weather-api", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void Values_AreReadOnceAtStartup()
    {
        // Rotation takes effect on restart: later configuration changes are not picked up.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Execution:Secrets:weather-api"] = "first"
        }).Build();
        var provider = new ConfigurationSecretProvider(configuration);

        configuration["Execution:Secrets:weather-api"] = "second";

        Assert.True(provider.TryGetSecret("weather-api", out var value));
        Assert.Equal("first", value);
    }

    [Fact]
    public void NoSection_MeansNoSecrets()
    {
        var provider = new ConfigurationSecretProvider(new ConfigurationBuilder().Build());

        Assert.False(provider.TryGetSecret("weather-api", out _));
    }

    private static ConfigurationSecretProvider Create(params (string Name, string Value)[] secrets) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(secrets.Select(secret => new KeyValuePair<string, string?>($"Execution:Secrets:{secret.Name}", secret.Value)))
            .Build());
}
