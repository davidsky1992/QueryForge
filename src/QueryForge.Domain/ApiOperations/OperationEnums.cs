namespace QueryForge.Domain.ApiOperations;

/// <summary>Upstream HTTP method. Milestone 1 supports GET only.</summary>
public enum OperationHttpMethod
{
    Get = 0
}

public enum ParameterLocation
{
    Path = 0,
    Query = 1
}

public enum ParameterType
{
    String = 0,
    Integer = 1,
    Number = 2,
    Boolean = 3
}
