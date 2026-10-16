namespace QueryForge.Application.Common;

/// <summary>
/// Raised by persistence when a write is rejected because the record changed after the client read it.
/// Keeps persistence-specific exception types out of the application layer.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
