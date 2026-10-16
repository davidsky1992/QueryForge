using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using QueryForge.Api.Http;

namespace QueryForge.Api.Security;

/// <summary>
/// Authenticates requests carrying an X-Api-Key header. The key value is never logged or echoed in responses.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ApiKeyStore _keyStore;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyStore keyStore)
        : base(options, logger, encoder)
    {
        _keyStore = keyStore;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var values) || values.Count == 0)
        {
            // No credentials: anonymous endpoints proceed; protected endpoints are challenged with 401.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Failure messages are logged by the base handler, so they must stay generic.
        if (values.Count > 1)
        {
            return Task.FromResult(AuthenticateResult.Fail("Multiple API key headers were sent."));
        }

        var identity = _keyStore.Find(values[0]);
        if (identity is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("The API key is not valid."));
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, identity.Name) };
        claims.AddRange(identity.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = $"{ApiKeyAuthenticationDefaults.Scheme} header=\"{ApiKeyAuthenticationDefaults.HeaderName}\"";

        return ProblemResponses.WriteAsync(
            Context,
            StatusCodes.Status401Unauthorized,
            "Authentication required",
            $"A valid API key must be sent in the {ApiKeyAuthenticationDefaults.HeaderName} header.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        return ProblemResponses.WriteAsync(
            Context,
            StatusCodes.Status403Forbidden,
            "Forbidden",
            "The API key does not grant access to this resource.");
    }
}
