using System;
using System.Collections.Generic;
using FlowOS.Core.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

public class RetryPolicyService : IRetryPolicyService
{
    public RetryPolicyPreviewDto Preview(
        int currentRetryCount,
        int maxRetries = 5,
        int baseDelaySeconds = 2,
        string strategy = "exponential",
        int maxDelaySeconds = 3600,
        string? errorMessage = null)
    {
        maxRetries = Math.Max(1, maxRetries);
        currentRetryCount = Math.Max(0, currentRetryCount);
        baseDelaySeconds = Math.Max(1, baseDelaySeconds);
        maxDelaySeconds = Math.Max(baseDelaySeconds, maxDelaySeconds);
        var normalizedStrategy = NormalizeStrategy(strategy);
        var classification = Classify(errorMessage);

        var planned = new List<RetryAttemptPlanDto>();
        for (int attempt = currentRetryCount + 1; attempt <= maxRetries; attempt++)
        {
            var delay = ComputeDelaySeconds(
                attemptNumber: attempt,
                baseDelaySeconds: baseDelaySeconds,
                strategy: normalizedStrategy,
                maxDelaySeconds: maxDelaySeconds);
            planned.Add(new RetryAttemptPlanDto(attempt, delay));
        }

        var shouldRetry = classification == "Transient" && currentRetryCount < maxRetries;
        return new RetryPolicyPreviewDto(
            Strategy: normalizedStrategy,
            MaxRetries: maxRetries,
            CurrentRetryCount: currentRetryCount,
            BaseDelaySeconds: baseDelaySeconds,
            MaxDelaySeconds: maxDelaySeconds,
            ShouldRetryNow: shouldRetry,
            Classification: classification,
            PlannedAttempts: planned,
            PreviewGeneratedAtUtc: DateTime.UtcNow);
    }

    private static int ComputeDelaySeconds(
        int attemptNumber,
        int baseDelaySeconds,
        string strategy,
        int maxDelaySeconds)
    {
        int delay = strategy switch
        {
            "constant" => baseDelaySeconds,
            "linear" => baseDelaySeconds * attemptNumber,
            _ => (int)Math.Pow(2, Math.Max(0, attemptNumber - 1)) * baseDelaySeconds
        };
        return Math.Min(maxDelaySeconds, Math.Max(1, delay));
    }

    private static string NormalizeStrategy(string strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy)) return "exponential";
        var normalized = strategy.Trim().ToLowerInvariant();
        return normalized is "exponential" or "linear" or "constant"
            ? normalized
            : "exponential";
    }

    private static string Classify(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "Unknown";
        var text = error.ToLowerInvariant();
        if (text.Contains("timeout") ||
            text.Contains("temporar") ||
            text.Contains("connection") ||
            text.Contains("socket") ||
            text.Contains("429") ||
            text.Contains("503"))
        {
            return "Transient";
        }

        return "Permanent";
    }
}
