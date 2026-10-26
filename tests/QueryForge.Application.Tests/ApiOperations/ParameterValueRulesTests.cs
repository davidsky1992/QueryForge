using System.Diagnostics;
using QueryForge.Application.ApiOperations;
using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.Tests.ApiOperations;

public sealed class ParameterValueRulesTests
{
    [Theory]
    [InlineData(ParameterType.String, "Hanoi")]
    [InlineData(ParameterType.Integer, "42")]
    [InlineData(ParameterType.Integer, "-7")]
    [InlineData(ParameterType.Number, "3.14")]
    [InlineData(ParameterType.Number, "-0.5")]
    [InlineData(ParameterType.Boolean, "true")]
    [InlineData(ParameterType.Boolean, "false")]
    public void ValidValues_Pass(ParameterType type, string value)
    {
        Assert.True(ParameterValueRules.TryValidate(Parameter(type), value, out var error), error);
    }

    [Theory]
    [InlineData(ParameterType.Integer, "4.2")]
    [InlineData(ParameterType.Integer, "1e3")]
    [InlineData(ParameterType.Integer, " 1")]
    [InlineData(ParameterType.Integer, "0x10")]
    [InlineData(ParameterType.Integer, "99999999999999999999")]
    [InlineData(ParameterType.Number, "1,5")]
    [InlineData(ParameterType.Number, "NaN")]
    [InlineData(ParameterType.Number, "1e3")]
    [InlineData(ParameterType.Boolean, "TRUE")]
    [InlineData(ParameterType.Boolean, "1")]
    public void ValuesOfTheWrongType_Fail(ParameterType type, string value)
    {
        Assert.False(ParameterValueRules.TryValidate(Parameter(type), value, out _));
    }

    [Theory]
    [InlineData("line\nbreak")]
    [InlineData("tab\there")]
    [InlineData("null\0byte")]
    [InlineData("crlf\r\nX-Injected: 1")]
    public void ControlCharacters_Fail(string value)
    {
        Assert.False(ParameterValueRules.TryValidate(Parameter(ParameterType.String), value, out _));
    }

    [Fact]
    public void LengthLimits_ApplyEvenWithoutMaxLength()
    {
        var unlimited = Parameter(ParameterType.String);
        var limited = Parameter(ParameterType.String, maxLength: 3);

        Assert.True(ParameterValueRules.TryValidate(unlimited, new string('a', 1024), out _));
        Assert.False(ParameterValueRules.TryValidate(unlimited, new string('a', 1025), out _));
        Assert.True(ParameterValueRules.TryValidate(limited, "abc", out _));
        Assert.False(ParameterValueRules.TryValidate(limited, "abcd", out _));
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("7", true)]
    [InlineData("8", false)]
    public void IntegerBounds_AreInclusive(string value, bool expected)
    {
        Assert.Equal(expected, ParameterValueRules.TryValidate(Parameter(ParameterType.Integer, minimum: 1, maximum: 7), value, out _));
    }

    [Fact]
    public void AllowedValues_AreCaseSensitive()
    {
        var parameter = Parameter(ParameterType.String, allowedValues: ["metric", "imperial"]);

        Assert.True(ParameterValueRules.TryValidate(parameter, "metric", out _));
        Assert.False(ParameterValueRules.TryValidate(parameter, "Metric", out _));
        Assert.False(ParameterValueRules.TryValidate(parameter, "kelvin", out _));
    }

    [Fact]
    public void Patterns_MustMatchTheWholeValue()
    {
        // Without anchoring, "[a-z]+" would match inside "abc;DROP".
        var parameter = Parameter(ParameterType.String, pattern: "[a-z]+");

        Assert.True(ParameterValueRules.TryValidate(parameter, "abc", out _));
        Assert.False(ParameterValueRules.TryValidate(parameter, "abc;DROP", out _));
        Assert.False(ParameterValueRules.TryValidate(parameter, "ABC", out _));
    }

    [Fact]
    public void AlternationPatterns_AreAnchoredAsAWhole()
    {
        // Anchoring as ^(?:a|b)$ rather than ^a|b$ so "xb" does not slip through the second branch.
        var parameter = Parameter(ParameterType.String, pattern: "a|b");

        Assert.True(ParameterValueRules.TryValidate(parameter, "b", out _));
        Assert.False(ParameterValueRules.TryValidate(parameter, "xb", out _));
        Assert.False(ParameterValueRules.TryValidate(parameter, "ax", out _));
    }

    [Fact]
    public void ReDoSPattern_EvaluatesInLinearTime()
    {
        var parameter = Parameter(ParameterType.String, pattern: "(a+)+b");
        var hostile = new string('a', 1000) + "!";
        var stopwatch = Stopwatch.StartNew();

        Assert.False(ParameterValueRules.TryValidate(parameter, hostile, out _));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void ErrorMessages_DoNotEchoTheValue()
    {
        Assert.False(ParameterValueRules.TryValidate(Parameter(ParameterType.Integer), "secret-value", out var error));

        Assert.DoesNotContain("secret-value", error);
    }

    private static OperationParameter Parameter(
        ParameterType type,
        int? maxLength = null,
        decimal? minimum = null,
        decimal? maximum = null,
        string? pattern = null,
        IReadOnlyList<string>? allowedValues = null) =>
        new("p", ParameterLocation.Query, type, isRequired: false, maxLength, minimum, maximum, pattern, allowedValues, defaultValue: null);
}
