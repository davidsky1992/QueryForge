using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueryForge.Api.Execution;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Api.Security;
using QueryForge.Api.Tests.Execution;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Application.ApiOperations;
using QueryForge.Application.Execution;
using QueryForge.Domain.ApiDefinitions;
using QueryForge.Domain.ApiOperations;
using QueryForge.Domain.Auditing;
using QueryForge.Infrastructure.Execution;
using QueryForge.Infrastructure.Persistence;

namespace QueryForge.Api.IntegrationTests.Execution;

/// <summary>
/// The execution endpoint through the full pipeline. "Public" upstreams are simulated: the guarded connector checks
/// a public address, and the socket for it is opened to a loopback server, so no test reaches the internet. Tests of
/// blocking use the real DNS resolver and socket connector.
/// </summary>
public sealed class ExecutionEndpointTests : ApiTestBase
{
    private const string SimulatedBaseUrl = "http://api.example.test";
    private const string Sentinel = "SENTINEL-5be0";

    public ExecutionEndpointTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task EndpointDoesNotExist_WhileExecutionIsDisabled()
    {
        // The default host uses the shipped configuration (Execution:Enabled=false). The operation exists, so a
        // present endpoint would answer (502 for the plain-HTTP base); an absent route gives an empty 404.
        await SeedWeatherAsync();
        using var executor = Factory.CreateClient(Factory.ExecutorApiKey);
        using var anonymous = Factory.CreateClient(apiKey: null);

        foreach (var client in new[] { executor, Client })
        {
            var response = await client.PostAsJsonAsync("/api/execute/weather/current", new { parameters = new { city = "Hanoi" } });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/execute/weather/current", new { })).StatusCode);
    }

    [Fact]
    public void ShippedPipeline_UsesTheHardenedHandlerRealResolverAndNoHttpLoggers()
    {
        var services = Factory.Services;

        Assert.IsType<SystemDnsResolver>(services.GetRequiredService<IDnsResolver>());
        Assert.IsType<SystemSocketConnector>(services.GetRequiredService<ISocketConnector>());
        var options = services.GetRequiredService<ExecutionOptions>();
        Assert.False(options.Enabled);
        Assert.False(options.AllowHttp);
        Assert.Equal([80, 443], options.AllowedPorts);
        var policy = services.GetRequiredService<DestinationPolicy>();
        Assert.True(policy.IsPortAllowed(443));
        Assert.False(policy.IsPortAllowed(8080));

        using var scope = services.CreateScope();
        Assert.IsType<HttpUpstreamClient>(scope.ServiceProvider.GetRequiredService<IUpstreamClient>());

        var chain = new List<HttpMessageHandler>();
        for (HttpMessageHandler? handler = services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(ExecutionServiceCollectionExtensions.UpstreamClientName);
             handler is not null;
             handler = (handler as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(handler);
        }

        Assert.Contains(chain, handler => handler is UpstreamHttpVersionHandler);
        Assert.DoesNotContain(chain, handler => handler.GetType().Name.Contains("Logging", StringComparison.Ordinal));
        var primary = Assert.IsType<SocketsHttpHandler>(chain[^1]);
        Assert.NotNull(primary.ConnectCallback);
        Assert.False(primary.UseProxy);
        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseCookies);
        Assert.Equal(DecompressionMethods.None, primary.AutomaticDecompression);
    }

    [Fact]
    public async Task Success_ReturnsTheEnvelope_AndSendsOnlyQueryForgeHeaders()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nX-Upstream-Secret: leak\r\nSet-Cookie: upstream=1", """{"temp":31}"""u8.ToArray());
        var simulator = new PublicUpstreamSimulator(server);
        await using var host = SimulatedHost(simulator);
        await SeedWeatherAsync();
        using var executor = CreateClient(host, Factory.ExecutorApiKey);
        using var request = ExecuteRequest("weather", "current", new { city = "Hanoi", days = 2 });
        request.Headers.Authorization = new("Bearer", "caller-token");
        request.Headers.Add("Cookie", "caller=1");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");
        request.Headers.Add("X-Custom", "caller");

        var response = await executor.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(200, envelope.RootElement.GetProperty("upstreamStatus").GetInt32());
        Assert.Equal("application/json", envelope.RootElement.GetProperty("contentType").GetString());
        Assert.Equal(31, envelope.RootElement.GetProperty("body").GetProperty("temp").GetInt32());
        Assert.True(envelope.RootElement.GetProperty("durationMs").GetInt64() >= 0);

        // Upstream headers are not passed through.
        Assert.False(response.Headers.Contains("X-Upstream-Secret"));
        Assert.False(response.Headers.Contains("Set-Cookie"));

        // Upstream saw exactly the registered request and only QueryForge's own headers.
        Assert.Equal([new IPEndPoint(PublicUpstreamSimulator.PublicAddress, 80)], simulator.Attempts);
        var head = Assert.Single(server.Requests);
        Assert.StartsWith("GET /v1/forecast/Hanoi?days=2 HTTP/1.1\r\n", head);
        Assert.Contains("\r\nHost: api.example.test", head);
        Assert.Contains("\r\nUser-Agent: QueryForge/1.0", head);
        foreach (var forbidden in new[] { "X-Api-Key", "Authorization", "Cookie", "X-Forwarded-For", "X-Custom" })
        {
            Assert.DoesNotContain(head.Split("\r\n").Skip(1), line => line.StartsWith(forbidden + ":", StringComparison.OrdinalIgnoreCase));
        }

        Assert.DoesNotContain(Factory.ExecutorApiKey, head);
        Assert.DoesNotContain("caller-token", head);
    }

    [Fact]
    public async Task Roles_OnlyExecutorAndAdminMayExecute()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 200 OK\r\nContent-Type: application/json", "{}"u8.ToArray());
        server.EnqueueResponse("HTTP/1.1 200 OK\r\nContent-Type: application/json", "{}"u8.ToArray());
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        await SeedWeatherAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Execute(host, null, new { city = "Hanoi" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Execute(host, Factory.ReaderApiKey, new { city = "Hanoi" })).StatusCode);
        Assert.Equal(0, server.AcceptedConnections); // rejected callers never cause an upstream call
        Assert.Equal(HttpStatusCode.OK, (await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Execute(host, Factory.AdminApiKey, new { city = "Hanoi" })).StatusCode);
    }

    [Fact]
    public async Task RejectedRequests_NeverReachTheUpstream()
    {
        await using var server = LoopbackServer.StartIPv4();
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        var definition = await SeedWeatherAsync();
        await CreateOperationAsync(definition.Id, "disabled", enabled: false);
        var bearer = await CreateDefinitionAsync("secured", SimulatedBaseUrl, ApiAuthenticationType.BearerToken);
        await CreateOperationAsync(bearer.Id, "current");

        var unknownParameter = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi", admin = "true" });
        var missingParameter = await Execute(host, Factory.ExecutorApiKey, new { });
        var unknownOperation = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" }, operation: "missing");
        var disabled = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" }, operation: "disabled");
        var authRequired = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" }, definition: "secured");
        var traversal = await Execute(host, Factory.ExecutorApiKey, new { city = ".." });

        Assert.True((await AssertProblemAsync(unknownParameter, HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("parameters.admin", out _));
        Assert.True((await AssertProblemAsync(missingParameter, HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("parameters.city", out _));
        await AssertProblemAsync(unknownOperation, HttpStatusCode.NotFound);
        await AssertCodeAsync(disabled, HttpStatusCode.Conflict, "operation_disabled");
        await AssertCodeAsync(authRequired, HttpStatusCode.Conflict, "upstream_auth_not_supported");
        await AssertProblemAsync(traversal, HttpStatusCode.BadRequest);
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Fact]
    public async Task ApiKeyDefinition_SendsOnlyTheConfiguredSecret_AndItNeverLeaks()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 200 OK\r\nContent-Type: application/json", """{"temp":31}"""u8.ToArray());
        var simulator = new PublicUpstreamSimulator(server);
        await using var host = SimulatedHost(simulator);
        // The upstream header has the same name as QueryForge's own X-Api-Key, so the caller's key must not leak into it.
        var definition = await CreateDefinitionAsync("keyed", SimulatedBaseUrl, ApiAuthenticationType.ApiKey, QueryForgeApiFactory.UpstreamSecretName, "X-API-Key");
        await CreateOperationAsync(definition.Id, "current");

        var response = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" }, definition: "keyed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var head = Assert.Single(server.Requests);
        var keyLines = head.Split("\r\n").Where(line => line.StartsWith("X-API-Key:", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal([$"X-API-Key: {Factory.UpstreamSecretValue}"], keyLines);
        Assert.DoesNotContain(Factory.ExecutorApiKey, head);
        Assert.Single(simulator.Attempts); // sent to the checked base URL host only

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Factory.UpstreamSecretValue, body);
        Assert.DoesNotContain(Factory.UpstreamSecretValue, string.Join(Environment.NewLine, response.Headers.Select(header => string.Join(",", header.Value))));
        Assert.DoesNotContain(Factory.UpstreamSecretValue, string.Join(Environment.NewLine, Factory.Logs.Messages));
        var audit = await WithDbContextAsync(dbContext => dbContext.AuditEvents.AsNoTracking().Where(item => item.Action == "operation.executed").ToListAsync());
        var stored = string.Join("|", audit.SelectMany(item => typeof(AuditEvent).GetProperties().Select(property => property.GetValue(item)?.ToString())));
        Assert.DoesNotContain(Factory.UpstreamSecretValue, stored);
    }

    [Fact]
    public async Task ApiKeyDefinition_UpstreamErrorsDoNotRevealTheSecret()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.HoldWhenNoResponse = true;
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        var definition = await CreateDefinitionAsync("keyed", SimulatedBaseUrl, ApiAuthenticationType.ApiKey, QueryForgeApiFactory.UpstreamSecretName, "X-API-Key");
        await CreateOperationAsync(definition.Id, "current", timeoutSeconds: 1);

        var response = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" }, definition: "keyed");

        await AssertCodeAsync(response, HttpStatusCode.GatewayTimeout, "upstream_timeout");
        Assert.DoesNotContain(Factory.UpstreamSecretValue, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(Factory.UpstreamSecretValue, string.Join(Environment.NewLine, Factory.Logs.Messages));
    }

    [Fact]
    public async Task ApiKeyDefinition_WithMissingSecretOrReference_IsRefusedBeforeAnyCall()
    {
        await using var server = LoopbackServer.StartIPv4();
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        var unset = await CreateDefinitionAsync("unset", SimulatedBaseUrl, ApiAuthenticationType.ApiKey, "not-configured", "X-API-Key");
        await CreateOperationAsync(unset.Id, "current");
        var noReference = await CreateDefinitionAsync("noref", SimulatedBaseUrl, ApiAuthenticationType.ApiKey);
        await CreateOperationAsync(noReference.Id, "current");

        var missingValue = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" }, definition: "unset");
        var missingReference = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" }, definition: "noref");

        await AssertCodeAsync(missingValue, HttpStatusCode.ServiceUnavailable, "upstream_secret_unavailable");
        Assert.DoesNotContain("not-configured", await missingValue.Content.ReadAsStringAsync());
        Assert.Null(missingValue.Headers.RetryAfter);
        await AssertCodeAsync(missingReference, HttpStatusCode.Conflict, "upstream_auth_not_configured");
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Fact]
    public async Task Redirects_AreReportedNotFollowed()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 302 Found\r\nLocation: http://169.254.169.254/latest/meta-data/");
        server.EnqueueResponse("HTTP/1.1 200 OK\r\nContent-Type: application/json", """{"followed":true}"""u8.ToArray());
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        await SeedWeatherAsync();

        var response = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(302, envelope.RootElement.GetProperty("upstreamStatus").GetInt32());
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("169.254.169.254", envelope.RootElement.GetRawText());
        Assert.Single(server.Requests);
    }

    [Theory]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Type: application/json", "{\"a\":", "upstream_invalid_response")]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Type: image/png", "png", "upstream_unsupported_content_type")]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Encoding: gzip", "{}", "upstream_invalid_response")]
    public async Task UnusableResponses_Are502WithACode(string head, string body, string code)
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse(head, Encoding.UTF8.GetBytes(body));
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        await SeedWeatherAsync();

        await AssertCodeAsync(await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" }), HttpStatusCode.BadGateway, code);
    }

    [Fact]
    public async Task OversizedResponse_Is502TooLarge()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 200 OK\r\nContent-Type: text/plain", new byte[2048]);
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        var definition = await CreateDefinitionAsync("weather", SimulatedBaseUrl);
        await CreateOperationAsync(definition.Id, "current", maxResponseBytes: 1024);

        await AssertCodeAsync(await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" }), HttpStatusCode.BadGateway, "upstream_response_too_large");
    }

    [Fact]
    public async Task SlowUpstream_Is504()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.HoldWhenNoResponse = true;
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        var definition = await CreateDefinitionAsync("weather", SimulatedBaseUrl);
        await CreateOperationAsync(definition.Id, "current", timeoutSeconds: 1);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var response = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" });

        await AssertCodeAsync(response, HttpStatusCode.GatewayTimeout, "upstream_timeout");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task RequestBody_IsBoundedAndMustBeJson()
    {
        await using var server = LoopbackServer.StartIPv4();
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        await SeedWeatherAsync();
        using var executor = CreateClient(host, Factory.ExecutorApiKey);

        var tooLarge = await executor.PostAsync("/api/execute/weather/current",
            new StringContent("{\"parameters\":{\"city\":\"" + new string('a', 17 * 1024) + "\"}}", Encoding.UTF8, "application/json"));
        var malformed = await executor.PostAsync("/api/execute/weather/current", new StringContent("{\"parameters\":", Encoding.UTF8, "application/json"));
        var notJson = await executor.PostAsync("/api/execute/weather/current", new StringContent("city=Hanoi", Encoding.UTF8, "text/plain"));
        var tooDeep = await executor.PostAsync("/api/execute/weather/current",
            new StringContent("{\"parameters\":{\"city\":" + new string('[', 20) + new string(']', 20) + "}}", Encoding.UTF8, "application/json"));

        await AssertCodeAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge, "request_too_large");
        await AssertProblemAsync(malformed, HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, notJson.StatusCode);
        await AssertProblemAsync(tooDeep, HttpStatusCode.BadRequest);
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Fact]
    public async Task ChunkedBodyWithoutLength_IsAlsoBounded()
    {
        await using var server = LoopbackServer.StartIPv4();
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        await SeedWeatherAsync();
        using var executor = CreateClient(host, Factory.ExecutorApiKey);
        var bytes = Encoding.UTF8.GetBytes("{\"parameters\":{\"city\":\"" + new string('a', 17 * 1024) + "\"}}");
        // The client sends no Content-Length. The in-process test server may still learn the length, so this checks
        // the end-to-end 413 only; the counting loop itself is covered by BoundedBodyReaderTests.
        var content = new StreamContent(new NonSeekableStream(bytes));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await executor.PostAsync("/api/execute/weather/current", content);

        Assert.Null(content.Headers.ContentLength);
        await AssertCodeAsync(response, HttpStatusCode.RequestEntityTooLarge, "request_too_large");
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.1")]
    [InlineData("localhost")]
    public async Task RealResolverAndSockets_BlockLoopbackUpstreams(string host)
    {
        await using var server = LoopbackServer.StartIPv4();
        // Real DNS and sockets; plain HTTP and the server's port are allowed, so only the address rules can block.
        await using var api = Factory.WithHost(new Dictionary<string, string>
        {
            ["Execution:Enabled"] = "true",
            ["Execution:AllowHttp"] = "true",
            ["Execution:AllowedPorts:0"] = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        var definition = await CreateDefinitionAsync("internal", $"http://{host}:{server.Port}");
        await CreateOperationAsync(definition.Id, "current");

        var response = await Execute(api, Factory.ExecutorApiKey, new { city = "Hanoi" }, definition: "internal");

        await AssertCodeAsync(response, HttpStatusCode.BadGateway, "upstream_destination_blocked");
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Fact]
    public async Task ShippedSchemeRule_BlocksPlainHttpUpstreams()
    {
        await using var server = LoopbackServer.StartIPv4();
        await using var host = Factory.WithHost(new Dictionary<string, string> { ["Execution:Enabled"] = "true" });
        await SeedWeatherAsync();

        await AssertCodeAsync(await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" }), HttpStatusCode.BadGateway, "upstream_destination_blocked");
    }

    [Fact]
    public async Task ConcurrencyCap_Rejects_WithRetryAfter()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.HoldWhenNoResponse = true;
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server), new Dictionary<string, string> { ["Execution:MaxConcurrentExecutions"] = "1" });
        var definition = await CreateDefinitionAsync("weather", SimulatedBaseUrl);
        await CreateOperationAsync(definition.Id, "current", timeoutSeconds: 10);
        using var first = CreateClient(host, Factory.ExecutorApiKey);
        using var cancelFirst = new CancellationTokenSource();

        var pending = first.SendAsync(ExecuteRequest("weather", "current", new { city = "Hanoi" }), cancelFirst.Token);
        await WaitForAsync(() => server.AcceptedConnections == 1);

        var rejected = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" });

        await AssertCodeAsync(rejected, HttpStatusCode.ServiceUnavailable, "execution_capacity_exceeded");
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal(1, server.AcceptedConnections);

        cancelFirst.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Executions_HaveTheirOwnRateLimit()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 200 OK\r\nContent-Type: application/json", "{}"u8.ToArray());
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server), new Dictionary<string, string>
        {
            ["RateLimiting:Execute:PermitLimit"] = "1",
            ["RateLimiting:Execute:WindowSeconds"] = "300"
        });
        await SeedWeatherAsync();

        Assert.Equal(HttpStatusCode.OK, (await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" })).StatusCode);
        var limited = await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi" });

        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests);
        Assert.Equal(1, server.AcceptedConnections);
    }

    [Fact]
    public async Task Executions_AreAudited_AndValuesStayOutOfAuditAndLogs()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 200 OK\r\nContent-Type: application/json", "{}"u8.ToArray());
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        await SeedWeatherAsync();

        Assert.Equal(HttpStatusCode.OK, (await Execute(host, Factory.ExecutorApiKey, new { city = Sentinel })).StatusCode);
        await Execute(host, Factory.ExecutorApiKey, new { city = "Hanoi", days = Sentinel }); // rejected: not an integer

        var events = await WithDbContextAsync(dbContext => dbContext.AuditEvents.AsNoTracking()
            .Where(item => item.Action == "operation.executed").OrderBy(item => item.OccurredAtUtc).ToListAsync());
        Assert.Equal([AuditOutcome.Succeeded, AuditOutcome.Rejected], events.Select(item => item.Outcome));
        Assert.All(events, item => Assert.Equal("test-executor", item.Actor));
        Assert.Equal(200, events[0].UpstreamStatus);
        Assert.NotNull(events[0].DurationMs);
        Assert.Equal("validation_error", events[1].ErrorCode);

        var stored = string.Join("|", events.SelectMany(item => typeof(AuditEvent).GetProperties().Select(property => property.GetValue(item)?.ToString())));
        Assert.DoesNotContain(Sentinel, stored);

        // The upstream URL contained the value; no log line may, even with every category at Trace. (MVC logs action
        // arguments at Trace, which is why the execute action reads its body itself instead of binding it.)
        Assert.Contains(Sentinel, Assert.Single(server.Requests));
        var logs = string.Join(Environment.NewLine, Factory.Logs.Messages);
        Assert.DoesNotContain(Sentinel, logs);
        Assert.DoesNotContain(Factory.ExecutorApiKey, logs);
    }

    [Fact]
    public async Task ClientDisconnect_CancelsTheUpstreamCallAndIsAudited()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.HoldWhenNoResponse = true;
        await using var host = SimulatedHost(new PublicUpstreamSimulator(server));
        var definition = await CreateDefinitionAsync("weather", SimulatedBaseUrl);
        await CreateOperationAsync(definition.Id, "current", timeoutSeconds: 30);
        using var executor = CreateClient(host, Factory.ExecutorApiKey);
        using var cancel = new CancellationTokenSource();

        var pending = executor.SendAsync(ExecuteRequest("weather", "current", new { city = "Hanoi" }), cancel.Token);
        await WaitForAsync(() => server.AcceptedConnections == 1);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        // Well before the 30 s operation timeout, the call is abandoned and recorded as cancelled.
        AuditEvent? recorded = null;
        await WaitForAsync(async () => (recorded = await WithDbContextAsync(dbContext => dbContext.AuditEvents.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Action == "operation.executed"))) is not null);
        Assert.NotNull(recorded);
        Assert.Equal(AuditOutcome.Failed, recorded.Outcome);
        Assert.Equal("client_cancelled", recorded.ErrorCode);
    }

    // ---- Helpers ----

    private WebApplicationFactory<Program> SimulatedHost(PublicUpstreamSimulator simulator, IReadOnlyDictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string>
        {
            ["Execution:Enabled"] = "true",
            // The simulated upstream speaks plain HTTP on loopback; port 80 is the default allowed port.
            ["Execution:AllowHttp"] = "true"
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            settings[key] = value;
        }

        return Factory.WithHost(settings, services =>
        {
            services.RemoveAll<IDnsResolver>();
            services.RemoveAll<ISocketConnector>();
            services.AddSingleton<IDnsResolver>(simulator);
            services.AddSingleton<ISocketConnector>(simulator);
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> host, string? apiKey)
    {
        var client = host.CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);
        }

        return client;
    }

    private static HttpRequestMessage ExecuteRequest(string definition, string operation, object parameters) =>
        new(HttpMethod.Post, $"/api/execute/{definition}/{operation}") { Content = JsonContent.Create(new { parameters }) };

    private static async Task<HttpResponseMessage> Execute(
        WebApplicationFactory<Program> host, string? apiKey, object parameters, string definition = "weather", string operation = "current")
    {
        using var client = CreateClient(host, apiKey);
        return await client.SendAsync(ExecuteRequest(definition, operation, parameters));
    }

    private async Task<ApiDefinitionDto> SeedWeatherAsync()
    {
        var definition = await CreateDefinitionAsync("weather", SimulatedBaseUrl);
        await CreateOperationAsync(definition.Id, "current");
        return definition;
    }

    private Task<ApiDefinitionDto> CreateDefinitionAsync(
        string slug, string baseUrl, ApiAuthenticationType authentication = ApiAuthenticationType.None, string? secretName = null, string? headerName = null) =>
        CreateAsync(new CreateApiDefinitionRequest($"API {slug}", slug, null, baseUrl, ApiDefinitionStatus.Active, authentication, null, null, secretName, headerName));

    private async Task CreateOperationAsync(Guid definitionId, string key, bool enabled = true, int timeoutSeconds = 5, int maxResponseBytes = 65536)
    {
        var request = new CreateApiOperationRequest(key, OperationHttpMethod.Get, "/v1/forecast/{city}",
        [
            new OperationParameterRequest("city", ParameterLocation.Path, ParameterType.String, Required: true, MaxLength: 64),
            new OperationParameterRequest("days", ParameterLocation.Query, ParameterType.Integer, Required: false, Minimum: 1, Maximum: 7)
        ], timeoutSeconds, maxResponseBytes, enabled);

        var response = await Client.PostAsJsonAsync($"{BasePath}/{definitionId}/operations", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = await AssertProblemAsync(response, status);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private async Task<T> WithDbContextAsync<T>(Func<QueryForgeDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<QueryForgeDbContext>());
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private static Task WaitForAsync(Func<bool> condition) => WaitForAsync(() => Task.FromResult(condition()));

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
    }
}
