using QueryForge.Application.ApiOperations;
using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.Tests.ApiOperations;

public sealed class ApiOperationValidatorTests
{
    private static readonly OperationParameterRequest CityPath = new("city", ParameterLocation.Path, ParameterType.String, Required: true, MaxLength: 64);

    [Fact]
    public void ValidRequest_HasNoIssues()
    {
        var request = new CreateApiOperationRequest(
            "current-weather",
            OperationHttpMethod.Get,
            "/v1/forecast/{city}",
            [
                CityPath,
                new("days", ParameterLocation.Query, ParameterType.Integer, Required: false, Minimum: 1, Maximum: 7, Default: "3"),
                new("units", ParameterLocation.Query, ParameterType.String, Required: false, AllowedValues: ["metric", "imperial"]),
                new("api-version", ParameterLocation.Query, ParameterType.String, Required: false, Pattern: "[0-9]{4}-[0-9]{2}-[0-9]{2}")
            ]);

        Assert.Empty(ApiOperationValidator.Validate(request));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/v1/items")]
    [InlineData("/v1/items/")]
    [InlineData("/a-b/c.d/e_f/~g")]
    [InlineData("/v1/{id}")]
    [InlineData("/v1/{id}/sub/{child}")]
    public void ValidPathTemplates_AreAccepted(string template)
    {
        Assert.Null(ApiOperationValidator.TryParsePathTemplate(template, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1/items")]                  // not rooted
    [InlineData("https://evil.test/x")]       // absolute URL
    [InlineData("//evil.test/x")]             // scheme-relative
    [InlineData("/v1//items")]                // empty segment
    [InlineData("/v1/../admin")]              // traversal
    [InlineData("/v1/./items")]
    [InlineData("/..")]
    [InlineData("/v1/items?admin=true")]      // query
    [InlineData("/v1/items#frag")]            // fragment
    [InlineData("/v1/%2e%2e/admin")]          // encoded traversal
    [InlineData("/v1/a%2Fb")]                 // encoded slash
    [InlineData("/v1\\..\\admin")]            // backslashes
    [InlineData("/user@evil.test")]           // userinfo-like
    [InlineData("/v1/prefix-{id}")]           // partial-segment placeholder
    [InlineData("/v1/{id}{other}")]
    [InlineData("/v1/{}")]
    [InlineData("/v1/{1id}")]
    [InlineData("/v1/{id}/{id}")]             // duplicate placeholder
    [InlineData("/v1/items with space")]
    [InlineData("/v1/é")]                     // non-ASCII
    public void InvalidPathTemplates_AreRejected(string template)
    {
        Assert.NotNull(ApiOperationValidator.TryParsePathTemplate(template, out _));
    }

    [Fact]
    public void OverlongPathTemplate_IsRejected()
    {
        Assert.NotNull(ApiOperationValidator.TryParsePathTemplate("/" + new string('a', ApiOperationValidator.PathTemplateMaxLength), out _));
    }

    [Fact]
    public void Placeholders_AreReturnedInOrder()
    {
        ApiOperationValidator.TryParsePathTemplate("/v1/{tenant}/items/{id}", out var placeholders);

        Assert.Equal(["tenant", "id"], placeholders);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Current-Weather")]
    [InlineData("current_weather")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    public void InvalidKeys_AreRejected(string key)
    {
        AssertIssue(Request() with { Key = key }, "key");
    }

    [Fact]
    public void UndefinedMethod_IsRejected()
    {
        AssertIssue(Request() with { Method = (OperationHttpMethod)99 }, "method");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(-5)]
    public void TimeoutOutOfRange_IsRejected(int timeoutSeconds)
    {
        AssertIssue(Request() with { TimeoutSeconds = timeoutSeconds }, "timeoutSeconds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1024 * 1024 + 1)]
    public void MaxResponseBytesOutOfRange_IsRejected(int maxResponseBytes)
    {
        AssertIssue(Request() with { MaxResponseBytes = maxResponseBytes }, "maxResponseBytes");
    }

    [Fact]
    public void Defaults_AreTenSecondsAnd256KiB()
    {
        var request = new CreateApiOperationRequest("k", OperationHttpMethod.Get, "/");

        Assert.Equal(10, request.TimeoutSeconds);
        Assert.Equal(256 * 1024, request.MaxResponseBytes);
        Assert.True(request.Enabled);
    }

    [Fact]
    public void PlaceholderWithoutPathParameter_IsRejected()
    {
        AssertIssue(Request("/v1/{city}", []), "pathTemplate");
    }

    [Fact]
    public void PathParameterWithoutPlaceholder_IsRejected()
    {
        AssertIssue(Request("/v1/forecast", [CityPath]), "parameters");
    }

    [Fact]
    public void PlaceholderMatching_IsCaseSensitive()
    {
        AssertIssue(Request("/v1/{City}", [CityPath]), "pathTemplate");
    }

    [Fact]
    public void QueryParameterNamedLikeAPlaceholder_DoesNotSatisfyIt()
    {
        AssertIssue(Request("/v1/{city}", [CityPath with { Location = ParameterLocation.Query, Required = false }]), "pathTemplate");
    }

    [Fact]
    public void OptionalPathParameter_IsRejected()
    {
        AssertIssue(Request("/v1/{city}", [CityPath with { Required = false }]), "parameters[0].required");
    }

    [Fact]
    public void DuplicateParameterNames_AreRejectedCaseInsensitively()
    {
        AssertIssue(Request("/", [Query("days"), Query("DAYS")]), "parameters[1].name");
    }

    [Fact]
    public void TooManyParameters_AreRejected()
    {
        var parameters = Enumerable.Range(0, ApiOperationValidator.MaxParameters + 1).Select(index => Query($"p{index}")).ToArray();

        AssertIssue(Request("/", parameters), "parameters");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1days")]
    [InlineData("days value")]
    [InlineData("days&admin")]
    [InlineData("days=1")]
    public void InvalidParameterNames_AreRejected(string name)
    {
        AssertIssue(Request("/", [Query(name)]), "parameters[0].name");
    }

    [Fact]
    public void UndefinedLocationOrType_IsRejected()
    {
        AssertIssue(Request("/", [Query("a") with { Location = (ParameterLocation)9 }]), "parameters[0].location");
        AssertIssue(Request("/", [Query("a") with { Type = (ParameterType)9 }]), "parameters[0].type");
    }

    [Fact]
    public void RequiredParameterWithDefault_IsRejected()
    {
        AssertIssue(Request("/", [Query("a") with { Required = true, Default = "x" }]), "parameters[0].default");
    }

    [Theory]
    [InlineData(ParameterType.Integer, 10, null, null, "parameters[0].maxLength")]
    [InlineData(ParameterType.String, 0, null, null, "parameters[0].maxLength")]
    [InlineData(ParameterType.String, 1025, null, null, "parameters[0].maxLength")]
    [InlineData(ParameterType.String, null, 1, null, "parameters[0].minimum")]
    [InlineData(ParameterType.Boolean, null, null, 5, "parameters[0].minimum")]
    [InlineData(ParameterType.Integer, null, 10, 1, "parameters[0].minimum")]
    public void ConstraintsMustFitTheType(ParameterType type, int? maxLength, int? minimum, int? maximum, string field)
    {
        AssertIssue(Request("/", [Query("a") with { Type = type, MaxLength = maxLength, Minimum = minimum, Maximum = maximum }]), field);
    }

    [Fact]
    public void FractionalBoundsOnIntegers_AreRejected()
    {
        AssertIssue(Request("/", [Query("a") with { Type = ParameterType.Integer, Minimum = 0.5m }]), "parameters[0].minimum");
    }

    [Theory]
    [InlineData("(a)\\1")]        // backreference: not supported by the linear-time engine
    [InlineData("(?=a)a")]        // lookahead
    [InlineData("(?<=a)b")]       // lookbehind
    [InlineData("[")]             // invalid
    [InlineData("")]
    public void UnsupportedOrInvalidPatterns_AreRejected(string pattern)
    {
        AssertIssue(Request("/", [Query("a") with { Pattern = pattern }]), "parameters[0].pattern");
    }

    [Fact]
    public void PatternOnNonString_IsRejected()
    {
        AssertIssue(Request("/", [Query("a") with { Type = ParameterType.Integer, Pattern = "[0-9]+" }]), "parameters[0].pattern");
    }

    [Fact]
    public void OverlongPattern_IsRejected()
    {
        AssertIssue(Request("/", [Query("a") with { Pattern = new string('a', ApiOperationValidator.PatternMaxLength + 1) }]), "parameters[0].pattern");
    }

    [Fact]
    public void CatastrophicBacktrackingPattern_IsAcceptedBecauseTheEngineIsLinear()
    {
        // "(a+)+$" is the classic ReDoS pattern; under NonBacktracking it is safe and evaluates quickly.
        var request = Request("/", [Query("a") with { Pattern = "(a+)+b", Default = "aab" }]);

        Assert.Empty(ApiOperationValidator.Validate(request));
    }

    [Fact]
    public void AllowedValues_MustBeUniqueNonEmptyAndBounded()
    {
        AssertIssue(Request("/", [Query("a") with { AllowedValues = ["x", "x"] }]), "parameters[0].allowedValues");
        AssertIssue(Request("/", [Query("a") with { AllowedValues = [""] }]), "parameters[0].allowedValues");
        AssertIssue(Request("/", [Query("a") with { AllowedValues = [new string('x', 257)] }]), "parameters[0].allowedValues");
        AssertIssue(Request("/", [Query("a") with { AllowedValues = Enumerable.Range(0, 51).Select(i => $"v{i}").ToArray() }]), "parameters[0].allowedValues");
    }

    [Fact]
    public void AllowedValues_MustSatisfyTheType()
    {
        AssertIssue(Request("/", [Query("a") with { Type = ParameterType.Integer, AllowedValues = ["1", "two"] }]), "parameters[0].allowedValues");
        AssertIssue(Request("/", [Query("a") with { Type = ParameterType.Integer, Maximum = 5, AllowedValues = ["1", "9"] }]), "parameters[0].allowedValues");
    }

    [Theory]
    [InlineData(ParameterType.Integer, "abc")]
    [InlineData(ParameterType.Integer, "1.5")]
    [InlineData(ParameterType.Number, "1,5")]
    [InlineData(ParameterType.Boolean, "yes")]
    [InlineData(ParameterType.Boolean, "True")]
    public void DefaultMustSatisfyTheType(ParameterType type, string defaultValue)
    {
        AssertIssue(Request("/", [Query("a") with { Type = type, Default = defaultValue }]), "parameters[0].default");
    }

    [Fact]
    public void DefaultMustSatisfyConstraints()
    {
        AssertIssue(Request("/", [Query("a") with { Type = ParameterType.Integer, Minimum = 1, Maximum = 7, Default = "8" }]), "parameters[0].default");
        AssertIssue(Request("/", [Query("a") with { AllowedValues = ["metric"], Default = "kelvin" }]), "parameters[0].default");
        AssertIssue(Request("/", [Query("a") with { Pattern = "[a-z]+", Default = "ABC" }]), "parameters[0].default");
        AssertIssue(Request("/", [Query("a") with { MaxLength = 2, Default = "abc" }]), "parameters[0].default");
    }

    [Fact]
    public void Update_RequiresAValidRowVersion()
    {
        var update = new UpdateApiOperationRequest("k", OperationHttpMethod.Get, "/", [], 10, 1024, true, RowVersion: null);

        var issues = ApiOperationValidator.Validate(update, out _);

        Assert.Contains(issues, issue => issue.Field == "rowVersion");
    }

    [Fact]
    public void Update_WithValidRowVersion_ParsesIt()
    {
        var update = new UpdateApiOperationRequest("k", OperationHttpMethod.Get, "/", [], 10, 1024, true, Convert.ToBase64String(new byte[8] { 0, 0, 0, 0, 0, 0, 0, 7 }));

        var issues = ApiOperationValidator.Validate(update, out var rowVersion);

        Assert.Empty(issues);
        Assert.Equal(7, rowVersion[7]);
    }

    [Fact]
    public void NullParameterEntry_IsRejected()
    {
        AssertIssue(Request("/", [null!]), "parameters[0]");
    }

    private static CreateApiOperationRequest Request(string template = "/v1/{city}", IReadOnlyList<OperationParameterRequest>? parameters = null) =>
        new("current-weather", OperationHttpMethod.Get, template, parameters ?? [CityPath]);

    private static OperationParameterRequest Query(string name) =>
        new(name, ParameterLocation.Query, ParameterType.String, Required: false);

    private static void AssertIssue(CreateApiOperationRequest request, string field)
    {
        var issues = ApiOperationValidator.Validate(request);

        Assert.True(issues.Any(issue => issue.Field == field), $"Expected an issue for '{field}', got: {string.Join("; ", issues.Select(issue => $"{issue.Field}: {issue.Message}"))}");
    }
}
