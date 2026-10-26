using Microsoft.AspNetCore.Mvc;
using QueryForge.Application.Common;
using QueryForge.Application.Execution;

namespace QueryForge.Api.Http;

/// <summary>Maps application error codes to ProblemDetails responses, shared by the management controllers.</summary>
internal static class ResultMapping
{
    public static ActionResult ToFailureResult(this ControllerBase controller, string? errorCode, string? errorMessage, IReadOnlyList<ValidationIssue> validationIssues)
    {
        return errorCode switch
        {
            Errors.Validation => controller.BadRequest(new ValidationProblemDetails(ToModelState(validationIssues))
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest
            }),
            Errors.NotFound => controller.NotFound(new ProblemDetails
            {
                Title = "Resource not found",
                Detail = errorMessage,
                Status = StatusCodes.Status404NotFound
            }),
            Errors.Conflict => controller.Conflict(new ProblemDetails
            {
                Title = "Conflict",
                Detail = errorMessage,
                Status = StatusCodes.Status409Conflict
            }),
            Errors.ConcurrencyConflict => controller.Conflict(new ProblemDetails
            {
                Title = "Concurrency conflict",
                Detail = errorMessage,
                Status = StatusCodes.Status409Conflict,
                Extensions = { ["code"] = Errors.ConcurrencyConflict }
            }),
            ExecutionErrors.OperationDisabled or ExecutionErrors.UpstreamAuthNotSupported => controller.Conflict(new ProblemDetails
            {
                Title = "Operation cannot be executed",
                Detail = errorMessage,
                Status = StatusCodes.Status409Conflict,
                Extensions = { ["code"] = errorCode }
            }),
            UpstreamErrors.DestinationBlocked or
            UpstreamErrors.Unreachable or
            UpstreamErrors.Timeout or
            UpstreamErrors.ResponseTooLarge or
            UpstreamErrors.UnsupportedContentType or
            UpstreamErrors.InvalidResponse or
            UpstreamErrors.CapacityExceeded => Gateway(errorCode, errorMessage),
            _ => controller.BadRequest(new ProblemDetails
            {
                Title = "Request failed",
                Detail = errorMessage,
                Status = StatusCodes.Status400BadRequest
            })
        };
    }

    // 502/503/504 with a fixed title per status and the error code; the detail is the application's fixed message.
    private static ObjectResult Gateway(string errorCode, string? errorMessage)
    {
        var status = UpstreamErrorStatusCodes.For(errorCode);
        var problem = new ProblemDetails
        {
            Title = status switch
            {
                StatusCodes.Status504GatewayTimeout => "Upstream timeout",
                StatusCodes.Status503ServiceUnavailable => "Execution capacity exceeded",
                _ => "Upstream request failed"
            },
            Detail = errorMessage,
            Status = status,
            Extensions = { ["code"] = errorCode }
        };

        return new ObjectResult(problem) { StatusCode = status };
    }

    public static ActionResult ToFailureResult<T>(this ControllerBase controller, Result<T> result) =>
        controller.ToFailureResult(result.ErrorCode, result.ErrorMessage, result.ValidationIssues);

    private static Dictionary<string, string[]> ToModelState(IReadOnlyList<ValidationIssue> issues)
    {
        return issues
            .GroupBy(issue => issue.Field)
            .ToDictionary(group => group.Key, group => group.Select(issue => issue.Message).ToArray());
    }
}
