using FlowOS.Application.Common.Interfaces;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class TenantEntitlementPolicyTests
{
    [Fact]
    public void PlanRequiredMessage_SeparatesAnUnpaidPlanFromAnExpiredKey()
    {
        Assert.Contains("Your API key is valid", TenantEntitlementPolicy.PlanRequiredMessage);
        Assert.Contains("not an expired key", TenantEntitlementPolicy.PlanRequiredMessage);
    }

    [Fact]
    public void PlanRequiredMessage_TellsTheTenantHowToActivate()
    {
        Assert.Contains("Request Plan Activation", TenantEntitlementPolicy.PlanRequiredMessage);
        Assert.Contains("payment reference", TenantEntitlementPolicy.PlanRequiredMessage);
        Assert.Contains("platform administrator", TenantEntitlementPolicy.PlanRequiredMessage);
    }

    [Fact]
    public void PlanRequiredMessage_KeepsTheCommercialPolicySentence()
    {
        Assert.Contains("MCP is included on every package", TenantEntitlementPolicy.PlanRequiredMessage);
        Assert.Contains("Starter through Scale, or Enterprise", TenantEntitlementPolicy.PlanRequiredMessage);
    }
}
