using QueryForge.Application.Execution;
using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.Tests.Execution;

public sealed class UpstreamUrlBuilderTests
{
    private static readonly ExecutionOptions Defaults = new();

    [Fact]
    public void BuildsPathAndQueryUnderTheBaseUrl()
    {
        var url = Build("https://api.example.test", "/v1/forecast/{city}", new() { ["city"] = "Hanoi", ["days"] = "2" });

        Assert.Equal("https://api.example.test/v1/forecast/Hanoi?days=2", url!.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://api.example.test/base")]
    [InlineData("https://api.example.test/base/")]
    public void KeepsTheBasePath(string baseUrl)
    {
        var url = Build(baseUrl, "/v1/forecast/{city}", new() { ["city"] = "Hanoi" });

        Assert.Equal("https://api.example.test/base/v1/forecast/Hanoi", url!.AbsoluteUri);
    }

    [Fact]
    public void RootTemplate_PointsAtTheBasePath()
    {
        Assert.Equal("https://api.example.test/base/", Build("https://api.example.test/base", "/", new())!.AbsoluteUri);
    }

    [Theory]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("a?admin=true", "a%3Fadmin%3Dtrue")]
    [InlineData("a#frag", "a%23frag")]
    [InlineData("a b", "a%20b")]
    [InlineData("100%", "100%25")]
    [InlineData("%2e%2e", "%252e%252e")]
    [InlineData("..%2F..%2Fadmin", "..%252F..%252Fadmin")]
    [InlineData("@evil.test", "%40evil.test")]
    [InlineData("//evil.test", "%2F%2Fevil.test")]
    [InlineData("...", "...")]
    [InlineData("Hà Nội", "H%C3%A0%20N%E1%BB%99i")]
    public void PathValues_AreEncodedIntoASingleSegment(string value, string expectedSegment)
    {
        var url = Build("https://api.example.test", "/v1/forecast/{city}", new() { ["city"] = value });

        Assert.NotNull(url);
        Assert.Equal("api.example.test", url.Host);
        Assert.Equal($"/v1/forecast/{expectedSegment}", url.AbsolutePath);
        Assert.Equal(string.Empty, url.Query);
        Assert.Equal(string.Empty, url.Fragment);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    public void DotAndEmptyPathValues_AreRejected(string value)
    {
        Assert.Null(Build("https://api.example.test", "/v1/forecast/{city}", new() { ["city"] = value }));
    }

    [Theory]
    [InlineData("1&admin=true", "?days=1%26admin%3Dtrue")]
    [InlineData("1#x", "?days=1%23x")]
    [InlineData("a b+c", "?days=a%20b%2Bc")]
    public void QueryValues_CannotAddParameters(string value, string expectedQuery)
    {
        var url = Build("https://api.example.test", "/v1/forecast/{city}", new() { ["city"] = "Hanoi", ["days"] = value });

        Assert.Equal(expectedQuery, url!.Query);
        Assert.Equal(string.Empty, url.Fragment);
    }

    [Fact]
    public void QueryParameters_FollowDeclarationOrder_AndOnlyIncludeBoundValues()
    {
        var operation = Operation("/items",
            Query("b"), Query("a"), Query("unset"));

        var url = UpstreamUrlBuilder.TryBuild("https://api.example.test", operation, new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }, Defaults);

        Assert.Equal("?b=2&a=1", url!.Query);
    }

    [Fact]
    public void ValuesForUndeclaredNames_AreIgnored()
    {
        // The binder already rejects unknown names; the builder must not use them either.
        var url = Build("https://api.example.test", "/v1/forecast/{city}", new() { ["city"] = "Hanoi", ["admin"] = "true" });

        Assert.Equal(string.Empty, url!.Query);
    }

    [Theory]
    [InlineData("http://api.example.test")]            // plain HTTP not allowed by default
    [InlineData("https://user:pass@api.example.test")] // userinfo
    [InlineData("https://api.example.test/?key=1")]    // query on the base
    [InlineData("https://api.example.test/#frag")]     // fragment on the base
    [InlineData("https://api.example.test:8443")]      // port not allowed
    [InlineData("ftp://api.example.test")]
    [InlineData("not a url")]
    [InlineData("/relative/base")]
    public void NonExecutableBaseUrls_AreRejected(string baseUrl)
    {
        Assert.Null(Build(baseUrl, "/v1/forecast/{city}", new() { ["city"] = "Hanoi" }));
    }

    [Fact]
    public void Http_IsAllowedOnlyWhenConfigured()
    {
        var options = new ExecutionOptions { AllowHttp = true };

        var url = UpstreamUrlBuilder.TryBuild("http://api.example.test", Operation("/items"), new Dictionary<string, string>(), options);

        Assert.Equal("http://api.example.test/items", url!.AbsoluteUri);
    }

    [Fact]
    public void ConfiguredPorts_AreAllowed()
    {
        var options = new ExecutionOptions { AllowedPorts = [8443] };

        Assert.NotNull(UpstreamUrlBuilder.TryBuild("https://api.example.test:8443", Operation("/items"), new Dictionary<string, string>(), options));
        Assert.Null(UpstreamUrlBuilder.TryBuild("https://api.example.test", Operation("/items"), new Dictionary<string, string>(), options));
    }

    private static Uri? Build(string baseUrl, string template, Dictionary<string, string> values)
    {
        var operation = Operation(template,
            new OperationParameter("city", ParameterLocation.Path, ParameterType.String, true, null, null, null, null, null, null),
            Query("days"));

        return UpstreamUrlBuilder.TryBuild(baseUrl, operation, values, Defaults);
    }

    private static ApiOperation Operation(string template, params OperationParameter[] parameters) =>
        ApiOperation.Create(Guid.NewGuid(), "k", OperationHttpMethod.Get, template, 10, 1024, true, parameters, DateTimeOffset.UnixEpoch);

    private static OperationParameter Query(string name) =>
        new(name, ParameterLocation.Query, ParameterType.String, false, null, null, null, null, null, null);
}
