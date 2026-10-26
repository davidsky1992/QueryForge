using QueryForge.Application.ApiOperations;
using QueryForge.Domain.ApiOperations;
using QueryForge.Domain.Auditing;

namespace QueryForge.Application.Tests.ApiOperations;

public sealed class ApiOperationDomainTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = Created.AddHours(1);

    [Fact]
    public void Create_NormalizesKeyAndTemplate()
    {
        var operation = ApiOperation.Create(Guid.NewGuid(), "  Current-Weather ", OperationHttpMethod.Get, " /v1/x ", 10, 1024, true, [], Created);

        Assert.Equal("current-weather", operation.Key);
        Assert.Equal("/v1/x", operation.PathTemplate);
        Assert.Equal(Created, operation.CreatedAtUtc);
        Assert.Null(operation.UpdatedAtUtc);
    }

    [Fact]
    public void Update_ReplacesParametersAndStampsTheUpdate()
    {
        var operation = ApiOperation.Create(Guid.NewGuid(), "k", OperationHttpMethod.Get, "/", 10, 1024, true, [Parameter("old")], Created);

        operation.Update("k", OperationHttpMethod.Get, "/", 5, 2048, false, [Parameter("a"), Parameter("b")], Updated);

        Assert.Equal(["a", "b"], operation.Parameters.Select(parameter => parameter.Name));
        Assert.Equal(5, operation.TimeoutSeconds);
        Assert.Equal(2048, operation.MaxResponseBytes);
        Assert.False(operation.IsEnabled);
        Assert.Equal(Updated, operation.UpdatedAtUtc);
    }

    [Fact]
    public void ToDto_ExposesAllFieldsAndBase64RowVersion()
    {
        var parameter = new OperationParameter("days", ParameterLocation.Query, ParameterType.Integer, false, null, 1, 7, null, ["1", "2"], "3");
        var operation = ApiOperation.Create(Guid.NewGuid(), "k", OperationHttpMethod.Get, "/", 10, 1024, true, [parameter], Created);

        var dto = operation.ToDto();

        var dtoParameter = Assert.Single(dto.Parameters);
        Assert.Equal("days", dtoParameter.Name);
        Assert.Equal(1, dtoParameter.Minimum);
        Assert.Equal(7, dtoParameter.Maximum);
        Assert.Equal(["1", "2"], dtoParameter.AllowedValues);
        Assert.Equal("3", dtoParameter.Default);
        Assert.Equal(string.Empty, dto.RowVersion);
        Assert.True(dto.Enabled);
    }

    [Fact]
    public void ToDto_ReportsDecimalsWithoutStorageScale()
    {
        // As read back from a decimal(38,10) column.
        var parameter = new OperationParameter("n", ParameterLocation.Query, ParameterType.Number, false, null, 1.0000000000m, 7.2500000000m, null, null, null);
        var operation = ApiOperation.Create(Guid.NewGuid(), "k", OperationHttpMethod.Get, "/", 10, 1024, true, [parameter], Created);

        var dto = Assert.Single(operation.ToDto().Parameters);

        Assert.Equal("1", dto.Minimum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("7.25", dto.Maximum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AuditEvent_BoundsEveryTextField()
    {
        var longText = new string('x', 500);

        var auditEvent = AuditEvent.Record(Created, longText, longText, longText, longText, AuditOutcome.Failed, errorCode: longText, traceId: longText);

        Assert.Equal(AuditEvent.ActorMaxLength, auditEvent.Actor.Length);
        Assert.Equal(AuditEvent.ActionMaxLength, auditEvent.Action.Length);
        Assert.Equal(AuditEvent.TargetTypeMaxLength, auditEvent.TargetType.Length);
        Assert.Equal(AuditEvent.TargetIdMaxLength, auditEvent.TargetId!.Length);
        Assert.Equal(AuditEvent.ErrorCodeMaxLength, auditEvent.ErrorCode!.Length);
        Assert.Equal(AuditEvent.TraceIdMaxLength, auditEvent.TraceId!.Length);
    }

    [Fact]
    public void AuditEvent_HasNoFieldForPayloadsOrCredentials()
    {
        // Guards the data-minimisation rule: adding a property for parameters, bodies or headers must be a
        // deliberate decision that also updates this test.
        var properties = typeof(AuditEvent).GetProperties().Select(property => property.Name).Order().ToArray();

        Assert.Equal(
            ["Action", "Actor", "DurationMs", "ErrorCode", "Id", "OccurredAtUtc", "Outcome", "TargetId", "TargetType", "TraceId", "UpstreamStatus"],
            properties);
    }

    private static OperationParameter Parameter(string name) =>
        new(name, ParameterLocation.Query, ParameterType.String, false, null, null, null, null, null, null);
}
