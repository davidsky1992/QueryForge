using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QueryForge.Api.RateLimiting;
using QueryForge.Api.Security;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Api.Controllers;

[ApiController]
[Route("api/api-definitions")]
[Authorize(Policy = AuthorizationPolicies.ReadApiDefinitions)]
[EnableRateLimiting(RateLimitPolicies.Reads)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public sealed class ApiDefinitionsController : ControllerBase
{
    private readonly IApiDefinitionService _apiDefinitionService;

    public ApiDefinitionsController(IApiDefinitionService apiDefinitionService)
    {
        _apiDefinitionService = apiDefinitionService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ApiDefinitionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ApiDefinitionDto>>> List(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] ApiDefinitionStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _apiDefinitionService.ListAsync(
            new ApiDefinitionQuery(pageNumber, pageSize, search, status),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiDefinitionDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _apiDefinitionService.GetByIdAsync(id, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.WriteApiDefinitions)]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    [ProducesResponseType(typeof(ApiDefinitionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiDefinitionDto>> Create(CreateApiDefinitionRequest request, CancellationToken cancellationToken)
    {
        var result = await _apiDefinitionService.CreateAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.WriteApiDefinitions)]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    [ProducesResponseType(typeof(ApiDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiDefinitionDto>> Update(Guid id, UpdateApiDefinitionRequest request, CancellationToken cancellationToken)
    {
        var result = await _apiDefinitionService.UpdateAsync(id, request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.WriteApiDefinitions)]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string? rowVersion, CancellationToken cancellationToken)
    {
        var result = await _apiDefinitionService.DeleteAsync(id, rowVersion, cancellationToken);

        return result.IsSuccess ? NoContent() : ToActionResult(result).Result!;
    }

    private ActionResult<ApiDefinitionDto> ToActionResult(Result<ApiDefinitionDto> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.ErrorCode switch
        {
            Errors.Validation => BadRequest(new ValidationProblemDetails(ToModelState(result.ValidationIssues))
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            }),
            Errors.NotFound => NotFound(new ProblemDetails
            {
                Title = "Resource not found",
                Detail = result.ErrorMessage,
                Status = StatusCodes.Status404NotFound
            }),
            Errors.Conflict => Conflict(new ProblemDetails
            {
                Title = "Conflict",
                Detail = result.ErrorMessage,
                Status = StatusCodes.Status409Conflict
            }),
            Errors.ConcurrencyConflict => Conflict(new ProblemDetails
            {
                Title = "Concurrency conflict",
                Detail = result.ErrorMessage,
                Status = StatusCodes.Status409Conflict,
                Extensions = { ["code"] = Errors.ConcurrencyConflict }
            }),
            _ => BadRequest(new ProblemDetails
            {
                Title = "Request failed",
                Detail = result.ErrorMessage,
                Status = StatusCodes.Status400BadRequest
            })
        };
    }

    private static Dictionary<string, string[]> ToModelState(IReadOnlyList<ValidationIssue> issues)
    {
        return issues
            .GroupBy(issue => issue.Field)
            .ToDictionary(group => group.Key, group => group.Select(issue => issue.Message).ToArray());
    }
}
