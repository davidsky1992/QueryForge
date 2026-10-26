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

    /// <summary>Read and modify API definitions and their operations, and view detailed health.</summary>
    public const string Admin = "admin";

    /// <summary>
    /// Execute operations only (once execution is enabled). Grants no access to the management endpoints: an
    /// executor key can neither read nor change definitions or operations.
    /// </summary>
    public const string Executor = "executor";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Reader, Admin, Executor };
}

public static class AuthorizationPolicies
{
    public const string ReadApiDefinitions = "ApiDefinitions.Read";
    public const string WriteApiDefinitions = "ApiDefinitions.Write";
    public const string ViewHealthDetails = "Health.Details";
    public const string ExecuteOperations = "Operations.Execute";
}
