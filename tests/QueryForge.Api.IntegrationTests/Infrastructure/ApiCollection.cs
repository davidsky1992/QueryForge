namespace QueryForge.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Tests in this collection share one SQL Server container and run sequentially, which keeps database resets safe.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<QueryForgeApiFactory>
{
    public const string Name = "QueryForge API";
}
