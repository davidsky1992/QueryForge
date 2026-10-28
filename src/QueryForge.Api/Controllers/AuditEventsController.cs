using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QueryForge.Api.Http;
using QueryForge.Api.RateLimiting;
using QueryForge.Api.Security;
using QueryForge.Application.Auditing;
using QueryForge.Application.Common;
using QueryForge.Domain.Auditing;

namespace QueryForge.Api.Controllers;

/// <summary>Read-only access to the audit trail, for admin keys only.</summary>
[ApiController]
[Route("api/audit-events")]
[Authorize(Policy = AuthorizationPolicies.ReadAuditEvents)]
[EnableRateLimiting(RateLimitPolicies.Reads)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public sealed class AuditEventsController : ControllerBase
{
    private readonly IAuditEventService _auditEventService;

    public AuditEventsController(IAuditEventService auditEventService)
    {
        _auditEventService = auditEventService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<AuditEventDto>>> List(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? action = null,
        [FromQuery] string? targetType = null,
        [FromQuery] string? targetId = null,
        [FromQuery] AuditOutcome? outcome = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _auditEventService.ListAsync(
            new AuditEventQuery(pageNumber, pageSize, action, targetType, targetId, outcome, from, to),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.ToFailureResult(result);
    }
}
