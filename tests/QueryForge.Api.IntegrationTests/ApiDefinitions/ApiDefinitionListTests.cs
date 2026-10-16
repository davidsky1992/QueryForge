using System.Net.Http.Json;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Api.IntegrationTests.ApiDefinitions;

public sealed class ApiDefinitionListTests : ApiTestBase
{
    public ApiDefinitionListTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task List_PagesResultsNewestFirst()
    {
        // Created sequentially, so each has a later CreatedAtUtc than the one before.
        for (var index = 1; index <= 5; index++)
        {
            await CreateAsync(NewDefinition($"paged-api-{index}"));
        }

        var firstPage = await ListAsync("pageNumber=1&pageSize=2");
        var lastPage = await ListAsync("pageNumber=3&pageSize=2");

        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(3, firstPage.TotalPages);
        Assert.Equal(["paged-api-5", "paged-api-4"], firstPage.Items.Select(item => item.Slug));
        Assert.Equal(["paged-api-1"], lastPage.Items.Select(item => item.Slug));
    }

    [Fact]
    public async Task List_NormalizesOutOfRangePaging()
    {
        await CreateAsync(NewDefinition("normalized-api"));

        var page = await ListAsync("pageNumber=0&pageSize=1000");

        Assert.Equal(1, page.PageNumber);
        Assert.Equal(PagedRequest.MaxPageSize, page.PageSize);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task List_FiltersBySearchAcrossNameSlugAndTags()
    {
        await CreateAsync(NewDefinition("shipping-api", name: "Shipping"));
        await CreateAsync(NewDefinition("tracking-api", name: "Parcel tracking") with { Tags = "shipping,logistics" });
        await CreateAsync(NewDefinition("weather-api", name: "Weather") with { Tags = "forecast" });

        var page = await ListAsync("search=shipping");

        Assert.Equal(2, page.TotalCount);
        Assert.DoesNotContain(page.Items, item => item.Slug == "weather-api");
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        await CreateAsync(NewDefinition("active-api", status: ApiDefinitionStatus.Active));
        await CreateAsync(NewDefinition("draft-api", status: ApiDefinitionStatus.Draft));

        var page = await ListAsync($"status={(int)ApiDefinitionStatus.Draft}");

        Assert.Equal(["draft-api"], page.Items.Select(item => item.Slug));
    }

    [Fact]
    public async Task List_WhenEmpty_ReturnsZeroPages()
    {
        var page = await ListAsync("pageNumber=1&pageSize=10");

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
    }

    private async Task<PagedResult<ApiDefinitionDto>> ListAsync(string query)
    {
        return (await Client.GetFromJsonAsync<PagedResult<ApiDefinitionDto>>($"{BasePath}?{query}"))!;
    }
}
