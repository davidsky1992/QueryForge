using System.Text.RegularExpressions;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.ApiDefinitions;

internal static partial class ApiDefinitionValidator
{
    private const int NameMaxLength = 160;
    private const int SlugMaxLength = 180;
    private const int DescriptionMaxLength = 1000;
    private const int UrlMaxLength = 2048;
    private const int TagsMaxLength = 500;
    private const int SecretNameMaxLength = 64;
    private const int HeaderNameMaxLength = 64;

    // Headers QueryForge controls, that frame or route the request, or that an upstream may trust for client identity.
    private static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Content-Type", "Transfer-Encoding", "Connection", "Keep-Alive", "Upgrade", "TE",
        "Trailer", "Expect", "Cookie", "User-Agent", "Accept", "Accept-Encoding", "Forwarded", "X-Real-IP"
    };

    private static readonly string[] ForbiddenHeaderPrefixes = ["Proxy-", "Sec-", "X-Forwarded-"];

    // SQL Server rowversion values are always 8 bytes.
    private const int RowVersionLength = 8;

    public static IReadOnlyList<ValidationIssue> Validate(CreateApiDefinitionRequest request)
    {
        return ValidateCore(
            request.Name,
            request.Slug,
            request.Description,
            request.BaseUrl,
            request.Status,
            request.AuthenticationType,
            request.DocumentationUrl,
            request.Tags,
            request.UpstreamSecretName,
            request.ApiKeyHeaderName);
    }

    public static IReadOnlyList<ValidationIssue> Validate(UpdateApiDefinitionRequest request, out byte[] parsedRowVersion)
    {
        var issues = ValidateCore(
            request.Name,
            request.Slug,
            request.Description,
            request.BaseUrl,
            request.Status,
            request.AuthenticationType,
            request.DocumentationUrl,
            request.Tags,
            request.UpstreamSecretName,
            request.ApiKeyHeaderName);

        return [.. issues, .. ValidateRowVersion(request.RowVersion, out parsedRowVersion)];
    }

    public static IReadOnlyList<ValidationIssue> ValidateRowVersion(string? rowVersion, out byte[] parsedRowVersion)
    {
        parsedRowVersion = [];

        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            return [new ValidationIssue(nameof(rowVersion), "rowVersion is required.")];
        }

        var buffer = new byte[RowVersionLength];
        if (!Convert.TryFromBase64String(rowVersion, buffer, out var bytesWritten) || bytesWritten != RowVersionLength)
        {
            return [new ValidationIssue(nameof(rowVersion), "rowVersion must be the base64 value returned by the API.")];
        }

        parsedRowVersion = buffer;
        return [];
    }

    private static IReadOnlyList<ValidationIssue> ValidateCore(
        string name,
        string slug,
        string? description,
        string baseUrl,
        ApiDefinitionStatus status,
        ApiAuthenticationType authenticationType,
        string? documentationUrl,
        string? tags,
        string? upstreamSecretName,
        string? apiKeyHeaderName)
    {
        var issues = new List<ValidationIssue>();
        ValidateUpstreamApiKey(authenticationType, upstreamSecretName, apiKeyHeaderName, issues);

        Required(name, nameof(name), issues);
        MaxLength(name, nameof(name), NameMaxLength, issues);

        Required(slug, nameof(slug), issues);
        MaxLength(slug, nameof(slug), SlugMaxLength, issues);
        if (!string.IsNullOrWhiteSpace(slug) && !SlugRegex().IsMatch(slug))
        {
            issues.Add(new ValidationIssue(nameof(slug), "Slug must contain only lowercase letters, numbers, and hyphens."));
        }

        Required(baseUrl, nameof(baseUrl), issues);
        MaxLength(baseUrl, nameof(baseUrl), UrlMaxLength, issues);
        AbsoluteHttpUrl(baseUrl, nameof(baseUrl), issues);

        MaxLength(description, nameof(description), DescriptionMaxLength, issues);
        MaxLength(documentationUrl, nameof(documentationUrl), UrlMaxLength, issues);
        AbsoluteHttpUrl(documentationUrl, nameof(documentationUrl), issues, allowEmpty: true);
        MaxLength(tags, nameof(tags), TagsMaxLength, issues);

        if (!Enum.IsDefined(status))
        {
            issues.Add(new ValidationIssue(nameof(status), "Status is not supported."));
        }

        if (!Enum.IsDefined(authenticationType))
        {
            issues.Add(new ValidationIssue(nameof(authenticationType), "Authentication type is not supported."));
        }

        return issues;
    }

    /// <summary>
    /// The secret reference is optional (a definition can be catalogued before its secret exists), but when present
    /// it needs both parts, only applies to ApiKey authentication, and the key always travels in a request header.
    /// </summary>
    private static void ValidateUpstreamApiKey(
        ApiAuthenticationType authenticationType,
        string? secretName,
        string? headerName,
        ICollection<ValidationIssue> issues)
    {
        var hasSecret = !string.IsNullOrWhiteSpace(secretName);
        var hasHeader = !string.IsNullOrWhiteSpace(headerName);
        if (!hasSecret && !hasHeader)
        {
            return;
        }

        if (authenticationType != ApiAuthenticationType.ApiKey)
        {
            issues.Add(new ValidationIssue("upstreamSecretName", "An upstream secret can only be configured for ApiKey authentication."));
            return;
        }

        if (!hasSecret || !hasHeader)
        {
            issues.Add(new ValidationIssue(hasSecret ? "apiKeyHeaderName" : "upstreamSecretName", "upstreamSecretName and apiKeyHeaderName must be set together."));
            return;
        }

        var name = secretName!.Trim();
        if (name.Length > SecretNameMaxLength || !SlugRegex().IsMatch(name.ToLowerInvariant()))
        {
            issues.Add(new ValidationIssue("upstreamSecretName", $"upstreamSecretName must be {SecretNameMaxLength} characters or fewer and contain only letters, numbers, and hyphens."));
        }

        var header = headerName!.Trim();
        if (header.Length > HeaderNameMaxLength || !HeaderTokenRegex().IsMatch(header))
        {
            issues.Add(new ValidationIssue("apiKeyHeaderName", $"apiKeyHeaderName must be a valid HTTP header name of {HeaderNameMaxLength} characters or fewer."));
        }
        else if (ForbiddenHeaders.Contains(header) || ForbiddenHeaderPrefixes.Any(prefix => header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new ValidationIssue("apiKeyHeaderName", "apiKeyHeaderName cannot be a header that QueryForge sets or that controls routing or framing."));
        }
    }

    private static void Required(string? value, string field, ICollection<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new ValidationIssue(field, $"{field} is required."));
        }
    }

    private static void MaxLength(string? value, string field, int maxLength, ICollection<ValidationIssue> issues)
    {
        if (value?.Length > maxLength)
        {
            issues.Add(new ValidationIssue(field, $"{field} must be {maxLength} characters or fewer."));
        }
    }

    private static void AbsoluteHttpUrl(string? value, string field, ICollection<ValidationIssue> issues, bool allowEmpty = false)
    {
        if (allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            issues.Add(new ValidationIssue(field, $"{field} must be an absolute HTTP or HTTPS URL."));
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    // RFC 9110 token characters.
    [GeneratedRegex("^[!#$%&'*+.^_`|~0-9A-Za-z-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderTokenRegex();
}
