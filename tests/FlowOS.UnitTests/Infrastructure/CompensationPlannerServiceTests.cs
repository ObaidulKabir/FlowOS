using System.Collections.Generic;
using System.Linq;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Infrastructure.Services;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class CompensationPlannerServiceTests
{
    [Fact]
    public void Plan_ShouldReturnBlockedSteps_WhenCompensationActionsMissing()
    {
        var service = new CompensationPlannerService();
        var request = new CompensationPathRequestDto(
            FailedStepId: "StepC",
            ExecutedStepIds: new List<string> { "StepA", "StepB", "StepC" },
            OnFailureActionsByStep: new Dictionary<string, List<CompensationActionDto>>
            {
                ["StepA"] = new()
                {
                    new CompensationActionDto("StepA", "OnFailure", "Webhook", "svc-a", null, null)
                },
                ["StepB"] = new(),
                ["StepC"] = new()
                {
                    new CompensationActionDto("StepC", "OnFailure", "Notification", "ops", null, null)
                }
            });

        var result = service.Plan(request);

        Assert.False(result.IsFullyCompensable);
        Assert.Equal(new[] { "StepC", "StepA" }, result.OrderedCompensations.Select(x => x.StepId).ToArray());
        Assert.Contains("StepB", result.BlockedSteps);
    }
}
