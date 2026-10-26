using QueryForge.Application.ApiOperations;
using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.Execution;

/// <summary>
/// Builds the upstream URL from the definition's BaseUrl, the operation's path template and already-validated
/// parameter values. Values are percent-encoded, so they can never add path segments, a query, a fragment or another
/// host; the result is re-parsed and must still be under the BaseUrl's scheme, host, port and path.
/// </summary>
public static class UpstreamUrlBuilder
{
    /// <summary>Returns the URL, or null when the BaseUrl or the result is not allowed for execution.</summary>
    public static Uri? TryBuild(string baseUrl, ApiOperation operation, IReadOnlyDictionary<string, string> values, ExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(options);

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || !IsExecutableBase(baseUri, options))
        {
            return null;
        }

        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var path = new System.Text.StringBuilder(basePath);
        foreach (var segment in operation.PathTemplate.Split('/').Skip(1))
        {
            path.Append('/');
            if (segment.Length > 1 && segment[0] == '{' && segment[^1] == '}')
            {
                var value = values.GetValueOrDefault(segment[1..^1]);

                // Encoded dots are still dots to many servers; empty values would collapse the segment.
                if (string.IsNullOrEmpty(value) || value is "." or "..")
                {
                    return null;
                }

                path.Append(Uri.EscapeDataString(value));
            }
            else
            {
                // Literal segments were restricted to unreserved characters at registration.
                path.Append(segment);
            }
        }

        var query = string.Join('&', operation.Parameters
            .Where(parameter => parameter.Location == ParameterLocation.Query && values.ContainsKey(parameter.Name))
            .Select(parameter => $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(values[parameter.Name])}"));

        var candidate = baseUri.GetLeftPart(UriPartial.Authority) + path + (query.Length > 0 ? "?" + query : string.Empty);
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var url))
        {
            return null;
        }

        // Defence in depth: whatever the parser made of the result, it must still point under the BaseUrl.
        var stillUnderBase =
            url.Scheme == baseUri.Scheme &&
            string.Equals(url.IdnHost, baseUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
            url.Port == baseUri.Port &&
            string.IsNullOrEmpty(url.UserInfo) &&
            string.IsNullOrEmpty(url.Fragment) &&
            (url.AbsolutePath == basePath + "/" || url.AbsolutePath.StartsWith(basePath + "/", StringComparison.Ordinal));

        return stillUnderBase ? url : null;
    }

    private static bool IsExecutableBase(Uri baseUri, ExecutionOptions options)
    {
        var schemeAllowed = baseUri.Scheme == Uri.UriSchemeHttps || (options.AllowHttp && baseUri.Scheme == Uri.UriSchemeHttp);

        return schemeAllowed &&
            string.IsNullOrEmpty(baseUri.UserInfo) &&
            string.IsNullOrEmpty(baseUri.Query) &&
            string.IsNullOrEmpty(baseUri.Fragment) &&
            options.AllowedPorts.Contains(baseUri.Port);
    }
}
