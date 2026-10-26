using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QueryForge.Api.Http;
using QueryForge.Api.RateLimiting;
using QueryForge.Api.Security;
using QueryForge.Application.ApiOperations;

namespace QueryForge.Api.Controllers;

/// <summary>
/// Management of an API definition's executable operations. Same permissions as the definitions themselves:
/// reader and admin keys can read, only admin keys can change, executor keys have no access.
/// </summary>
[ApiController]
[Route("api/api-definitions/{definitionId:guid}/operations")]
[Authorize(Policy = AuthorizationPolicies.ReadApiDefinitions)]
[EnableRateLimiting(RateLimitPolicies.Reads)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public sealed class ApiOperationsController : ControllerBase
{
    private readonly IApiOperationService _operationService;

    public ApiOperationsController(IApiOperationService operationService)
    {
        _operationService = operationService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ApiOperationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ApiOperationDto>>> List(Guid definitionId, CancellationToken cancellationToken)
    {
        var result = await _operationService.ListAsync(definitionId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.ToFailureResult(result);
    }

    [HttpGet("{operationId:guid}")]
    [ProducesResponseType(typeof(ApiOperationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiOperationDto>> GetById(Guid definitionId, Guid operationId, CancellationToken cancellationToken)
    {
        var result = await _operationService.GetByIdAsync(definitionId, operationId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.ToFailureResult(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.WriteApiDefinitions)]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    [ProducesResponseType(typeof(ApiOperationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiOperationDto>> Create(Guid definitionId, CreateApiOperationRequest request, CancellationToken cancellationToken)
    {
        var result = await _operationService.CreateAsync(definitionId, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToFailureResult(result);
        }

        return CreatedAtAction(nameof(GetById), new { definitionId, operationId = result.Value!.Id }, result.Value);
    }

    [HttpPut("{operationId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.WriteApiDefinitions)]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    [ProducesResponseType(typeof(ApiOperationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiOperationDto>> Update(Guid definitionId, Guid operationId, UpdateApiOperationRequest request, CancellationToken cancellationToken)
    {
        var result = await _operationService.UpdateAsync(definitionId, operationId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.ToFailureResult(result);
    }

    [HttpDelete("{operationId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.WriteApiDefinitions)]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid definitionId, Guid operationId, [FromQuery] string? rowVersion, CancellationToken cancellationToken)
    {
        var result = await _operationService.DeleteAsync(definitionId, operationId, rowVersion, cancellationToken);

        return result.IsSuccess ? NoContent() : this.ToFailureResult(result);
    }
}
