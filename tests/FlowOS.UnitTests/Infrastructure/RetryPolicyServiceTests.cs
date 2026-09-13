using FlowOS.Infrastructure.Services;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class RetryPolicyServiceTests
{
    [Fact]
    public void Preview_ExponentialPolicy_ProducesDoublingSchedule()
    {
        var service = new RetryPolicyService();

        var preview = service.Preview(
            currentRetryCount: 1,
            maxRetries: 4,
            baseDelaySeconds: 2,
            strategy: "exponential",
            maxDelaySeconds: 3600,
            errorMessage: "HTTP 503 timeout");

        Assert.Equal("exponential", preview.Strategy);
        Assert.Equal("Transient", preview.Classification);
        Assert.True(preview.ShouldRetryNow);
        Assert.Equal(3, preview.PlannedAttempts.Count);
        Assert.Equal(2, preview.PlannedAttempts[0].AttemptNumber);
        Assert.Equal(4, preview.PlannedAttempts[0].DelaySeconds);
        Assert.Equal(3, preview.PlannedAttempts[1].AttemptNumber);
        Assert.Equal(8, preview.PlannedAttempts[1].DelaySeconds);
        Assert.Equal(4, preview.PlannedAttempts[2].AttemptNumber);
        Assert.Equal(16, preview.PlannedAttempts[2].DelaySeconds);
    }

    [Fact]
    public void Preview_PermanentFailure_DisablesRetryRecommendation()
    {
        var service = new RetryPolicyService();

        var preview = service.Preview(
            currentRetryCount: 0,
            maxRetries: 3,
            baseDelaySeconds: 5,
            strategy: "linear",
            maxDelaySeconds: 60,
            errorMessage: "Validation failed: malformed payload");

        Assert.Equal("Permanent", preview.Classification);
        Assert.False(preview.ShouldRetryNow);
        Assert.Equal(3, preview.PlannedAttempts.Count);
        Assert.Equal(5, preview.PlannedAttempts[0].DelaySeconds);
        Assert.Equal(10, preview.PlannedAttempts[1].DelaySeconds);
        Assert.Equal(15, preview.PlannedAttempts[2].DelaySeconds);
    }
}
