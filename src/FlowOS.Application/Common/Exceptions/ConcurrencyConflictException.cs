using System;

namespace FlowOS.Application.Common.Exceptions;

/// <summary>
/// A write lost a race with another write to the same record, or repeated an idempotent
/// request that is still being processed. The caller should re-read and retry; the API answers
/// HTTP 409 Conflict with <see cref="Code"/> (CR-06, CR-10).
/// Derives from <see cref="InvalidOperationException"/> so existing callers keep working.
/// </summary>
public class ConcurrencyConflictException : InvalidOperationException
{
    /// <summary>Another request changed the record after it was read.</summary>
    public const string ConcurrentUpdate = "CONCURRENT-UPDATE";

    /// <summary>A request with the same idempotency key is still being processed.</summary>
    public const string IdempotencyInProgress = "IDEMPOTENCY-IN-PROGRESS";

    public string Code { get; }

    public ConcurrencyConflictException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }
}
