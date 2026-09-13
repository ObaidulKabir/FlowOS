using System;
using System.Collections.Generic;

namespace FlowOS.Core.Common.Interfaces;

public record RetryAttemptPlanDto(
    int AttemptNumber,
    int DelaySeconds);

public record RetryPolicyPreviewDto(
    string Strategy,
    int MaxRetries,
    int CurrentRetryCount,
    int BaseDelaySeconds,
    int MaxDelaySeconds,
    bool ShouldRetryNow,
    string Classification,
    List<RetryAttemptPlanDto> PlannedAttempts,
    DateTime PreviewGeneratedAtUtc);

public interface IRetryPolicyService
{
    RetryPolicyPreviewDto Preview(
        int currentRetryCount,
        int maxRetries = 5,
        int baseDelaySeconds = 2,
        string strategy = "exponential",
        int maxDelaySeconds = 3600,
        string? errorMessage = null);
}
