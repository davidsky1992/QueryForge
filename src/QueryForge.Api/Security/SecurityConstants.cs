namespace QueryForge.Api.Security;

public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}

public static class ApiKeyRoles
{
    /// <summary>Read API definitions.</summary>
    public const string Reader = "reader";

    /// <summary>Read and modify API definitions and view detailed health.</summary>
    public const string Admin = "admin";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Reader, Admin };
}

public static class AuthorizationPolicies
{
    public const string ReadApiDefinitions = "ApiDefinitions.Read";
    public const string WriteApiDefinitions = "ApiDefinitions.Write";
    public const string ViewHealthDetails = "Health.Details";
}
