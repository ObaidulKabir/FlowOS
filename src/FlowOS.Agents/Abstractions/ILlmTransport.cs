using System.Net.Http;

namespace FlowOS.Agents.Abstractions;

public sealed record LlmTransportResult(
    bool Success,
    string? Content,
    int? HttpStatusCode,
    string? ProviderRequestId,
    string? FailureCode,
    int AttemptCount = 1);

public interface ILlmTransport
{
    Task<LlmTransportResult> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default);
}

public static class LlmTelemetrySanitizer
{
    private const int MaximumRequestIdLength = 200;

    public static string? SanitizeRequestId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var candidate = value.Trim();
        if (candidate.Length > MaximumRequestIdLength)
            return null;

        foreach (var character in candidate)
        {
            if (char.IsLetterOrDigit(character) ||
                character is '-' or '_' or '.' or ':')
            {
                continue;
            }

            return null;
        }

        return candidate;
    }
}
