using System.Text.Json;
using QueryForge.Application.Execution;
using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.Tests.Execution;

public sealed class ParameterBinderTests
{
    private static readonly ApiOperation Operation = ApiOperation.Create(Guid.NewGuid(), "k", OperationHttpMethod.Get, "/v1/{city}", 10, 1024, true,
    [
        new OperationParameter("city", ParameterLocation.Path, ParameterType.String, true, 64, null, null, null, null, null),
        new OperationParameter("days", ParameterLocation.Query, ParameterType.Integer, false, null, 1, 7, null, null, "3"),
        new OperationParameter("metric", ParameterLocation.Query, ParameterType.Boolean, false, null, null, null, null, null, null),
        new OperationParameter("ratio", ParameterLocation.Query, ParameterType.Number, false, null, null, null, null, null, null)
    ], DateTimeOffset.UnixEpoch);

    [Fact]
    public void ValidValues_AreBoundAsStrings_AndDefaultsApplied()
    {
        var issues = Bind("""{"city":"Hanoi","metric":true,"ratio":2.5}""", out var values);

        Assert.Empty(issues);
        Assert.Equal("Hanoi", values["city"]);
        Assert.Equal("3", values["days"]);          // default
        Assert.Equal("true", values["metric"]);
        Assert.Equal("2.5", values["ratio"]);
    }

    [Fact]
    public void NumbersMayBeSentAsJsonNumbersOrStrings()
    {
        Assert.Empty(Bind("""{"city":"Hanoi","days":5}""", out var asNumber));
        Assert.Empty(Bind("""{"city":"Hanoi","days":"5"}""", out var asString));

        Assert.Equal("5", asNumber["days"]);
        Assert.Equal("5", asString["days"]);
    }

    [Fact]
    public void OptionalWithoutDefault_IsOmitted()
    {
        Bind("""{"city":"Hanoi"}""", out var values);

        Assert.False(values.ContainsKey("metric"));
    }

    [Fact]
    public void MissingRequired_IsRejected()
    {
        AssertIssue(Bind("""{}""", out _), "parameters.city", "required");
    }

    [Fact]
    public void NullRequestParameters_AreTreatedAsEmpty()
    {
        AssertIssue(ParameterBinder.Bind(Operation, null, out _), "parameters.city", "required");
    }

    [Theory]
    [InlineData("""{"city":"Hanoi","admin":"true"}""", "parameters.admin")]
    [InlineData("""{"city":"Hanoi","City":"Paris"}""", "parameters.City")]    // names are case-sensitive
    public void UnknownParameters_AreRejectedNotIgnored(string json, string field)
    {
        AssertIssue(Bind(json, out _), field, "Unknown");
    }

    [Fact]
    public void UnknownNamesThatDoNotLookLikeNames_AreNotEchoed()
    {
        var issues = Bind("""{"city":"Hanoi","x\u0000<script>":"1"}""", out _);

        var issue = Assert.Single(issues);
        Assert.Equal("parameters", issue.Field);
        Assert.DoesNotContain("<script>", issue.Message);
    }

    [Theory]
    [InlineData("""{"city":null}""")]
    [InlineData("""{"city":{"a":1}}""")]
    [InlineData("""{"city":["a"]}""")]
    public void NonScalarValues_AreRejected(string json)
    {
        AssertIssue(Bind(json, out _), "parameters.city", "string, number or boolean");
    }

    [Theory]
    [InlineData("""{"city":"Hanoi","days":0}""")]
    [InlineData("""{"city":"Hanoi","days":8}""")]
    [InlineData("""{"city":"Hanoi","days":2.5}""")]
    [InlineData("""{"city":"Hanoi","metric":"yes"}""")]
    [InlineData("""{"city":"Hanoi","ratio":1e3}""")]
    public void ValuesBreakingTheRules_AreRejected(string json)
    {
        Assert.NotEmpty(Bind(json, out _));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    public void DotAndEmptyPathValues_AreRejected(string value)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["city"] = value });

        AssertIssue(Bind(json, out _), "parameters.city", "path segment");
    }

    [Fact]
    public void DotsInsideAValue_AreAllowed()
    {
        Assert.Empty(Bind("""{"city":"St. Louis..."}""", out _));
    }

    [Fact]
    public void LengthLimit_IsEnforced()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["city"] = new string('a', 65) });

        AssertIssue(Bind(json, out _), "parameters.city", "64");
    }

    [Fact]
    public void Messages_NeverRepeatTheValue()
    {
        var issues = Bind("""{"city":"Hanoi","days":"SECRET-1234"}""", out _);

        Assert.All(issues, issue => Assert.DoesNotContain("SECRET-1234", issue.Message));
    }

    [Fact]
    public void TooManyParameters_AreRejected()
    {
        var json = JsonSerializer.Serialize(Enumerable.Range(0, 21).ToDictionary(index => $"p{index}", _ => "x"));

        AssertIssue(Bind(json, out _), "parameters", "At most");
    }

    private static IReadOnlyList<QueryForge.Application.Common.ValidationIssue> Bind(string json, out IReadOnlyDictionary<string, string> values) =>
        ParameterBinder.Bind(Operation, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json), out values);

    private static void AssertIssue(IReadOnlyList<QueryForge.Application.Common.ValidationIssue> issues, string field, string messagePart)
    {
        Assert.Contains(issues, issue => issue.Field == field && issue.Message.Contains(messagePart, StringComparison.Ordinal));
    }
}
