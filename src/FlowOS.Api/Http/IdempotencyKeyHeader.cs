using Microsoft.AspNetCore.Http;

namespace FlowOS.Api.Http;

/// <summary>
/// Reads the standard <c>Idempotency-Key</c> request header for write endpoints (CR-10).
/// A repeated request with the same key returns the first result instead of acting twice;
/// a repeat that arrives while the first is still running gets 409 Conflict.
/// The header and a JSON body field <c>idempotencyKey</c> may both be sent only if they match.
/// </summary>
public static class IdempotencyKeyHeader
{
    public const string Name = "Idempotency-Key";

    /// <summary>Matches the IdempotencyRecords.IdempotencyKey column length.</summary>
    public const int MaxLength = 200;

    public static bool TryResolve(HttpRequest request, string? bodyKey, out string? key, out string? error)
    {
        key = null;
        error = null;

        var values = request.Headers[Name];
        if (values.Count > 1)
        {
            error = $"Send one {Name} header, not {values.Count}.";
            return false;
        }

        var headerKey = values.Count == 1 ? values[0]?.Trim() : null;
        if (string.IsNullOrEmpty(headerKey))
            headerKey = null;
        var normalizedBodyKey = string.IsNullOrWhiteSpace(bodyKey) ? null : bodyKey.Trim();

        if (headerKey != null && normalizedBodyKey != null && headerKey != normalizedBodyKey)
        {
            error = $"The {Name} header and the body's idempotencyKey differ. Send one, or the same value in both.";
            return false;
        }

        key = headerKey ?? normalizedBodyKey;
        if (key != null && key.Length > MaxLength)
        {
            error = $"{Name} must be at most {MaxLength} characters.";
            key = null;
            return false;
        }

        return true;
    }
}
