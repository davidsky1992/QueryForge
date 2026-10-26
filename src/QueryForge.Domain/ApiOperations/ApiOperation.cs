namespace QueryForge.Domain.ApiOperations;

/// <summary>
/// One executable upstream call under an API definition. The definition supplies the host (BaseUrl) and upstream
/// authentication; the operation adds the method, a relative path template and the parameters a caller may supply.
/// </summary>
public sealed class ApiOperation
{
    private readonly List<OperationParameter> _parameters = [];

    private ApiOperation()
    {
    }

    private ApiOperation(Guid id, Guid apiDefinitionId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        ApiDefinitionId = apiDefinitionId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid ApiDefinitionId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public OperationHttpMethod Method { get; private set; }
    public string PathTemplate { get; private set; } = string.Empty;
    public int TimeoutSeconds { get; private set; }
    public int MaxResponseBytes { get; private set; }
    public bool IsEnabled { get; private set; }
    public IReadOnlyList<OperationParameter> Parameters => _parameters;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static ApiOperation Create(
        Guid apiDefinitionId,
        string key,
        OperationHttpMethod method,
        string pathTemplate,
        int timeoutSeconds,
        int maxResponseBytes,
        bool isEnabled,
        IEnumerable<OperationParameter> parameters,
        DateTimeOffset createdAtUtc)
    {
        var operation = new ApiOperation(Guid.NewGuid(), apiDefinitionId, createdAtUtc);
        operation.Apply(key, method, pathTemplate, timeoutSeconds, maxResponseBytes, isEnabled, parameters);
        return operation;
    }

    public void Update(
        string key,
        OperationHttpMethod method,
        string pathTemplate,
        int timeoutSeconds,
        int maxResponseBytes,
        bool isEnabled,
        IEnumerable<OperationParameter> parameters,
        DateTimeOffset updatedAtUtc)
    {
        Apply(key, method, pathTemplate, timeoutSeconds, maxResponseBytes, isEnabled, parameters);

        // Always touching the operation row means a parameter-only change still bumps RowVersion and is
        // covered by the optimistic concurrency check.
        UpdatedAtUtc = updatedAtUtc;
    }

    private void Apply(
        string key,
        OperationHttpMethod method,
        string pathTemplate,
        int timeoutSeconds,
        int maxResponseBytes,
        bool isEnabled,
        IEnumerable<OperationParameter> parameters)
    {
        Key = key.Trim().ToLowerInvariant();
        Method = method;
        PathTemplate = pathTemplate.Trim();
        TimeoutSeconds = timeoutSeconds;
        MaxResponseBytes = maxResponseBytes;
        IsEnabled = isEnabled;
        _parameters.Clear();
        _parameters.AddRange(parameters);
    }
}
