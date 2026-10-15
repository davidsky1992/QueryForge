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
            request.Tags);
    }

    public static IReadOnlyList<ValidationIssue> Validate(UpdateApiDefinitionRequest request)
    {
        return ValidateCore(
            request.Name,
            request.Slug,
            request.Description,
            request.BaseUrl,
            request.Status,
            request.AuthenticationType,
            request.DocumentationUrl,
            request.Tags);
    }

    private static IReadOnlyList<ValidationIssue> ValidateCore(
        string name,
        string slug,
        string? description,
        string baseUrl,
        ApiDefinitionStatus status,
        ApiAuthenticationType authenticationType,
        string? documentationUrl,
        string? tags)
    {
        var issues = new List<ValidationIssue>();

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
}
