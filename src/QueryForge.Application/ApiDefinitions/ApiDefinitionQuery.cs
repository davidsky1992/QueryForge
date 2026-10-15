using QueryForge.Application.Common;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.ApiDefinitions;

public sealed record ApiDefinitionQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    ApiDefinitionStatus? Status = null)
    : PagedRequest(PageNumber, PageSize);
