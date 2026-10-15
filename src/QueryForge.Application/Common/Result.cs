namespace QueryForge.Application.Common;

public sealed record ValidationIssue(string Field, string Message);

public sealed class Result<T>
{
    private Result(T? value, bool isSuccess, string? errorCode, string? errorMessage, IReadOnlyList<ValidationIssue> validationIssues)
    {
        Value = value;
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ValidationIssues = validationIssues;
    }

    public T? Value { get; }
    public bool IsSuccess { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public IReadOnlyList<ValidationIssue> ValidationIssues { get; }

    public static Result<T> Success(T value)
    {
        return new Result<T>(value, true, null, null, []);
    }

    public static Result<T> Failure(string errorCode, string errorMessage)
    {
        return new Result<T>(default, false, errorCode, errorMessage, []);
    }

    public static Result<T> Invalid(IReadOnlyList<ValidationIssue> validationIssues)
    {
        return new Result<T>(default, false, Errors.Validation, "One or more validation errors occurred.", validationIssues);
    }
}
