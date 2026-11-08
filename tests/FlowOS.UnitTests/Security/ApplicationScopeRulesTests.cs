using FlowOS.Core.Security;

namespace FlowOS.UnitTests.Security;

/// <summary>Applications (4/4): the pure access rule behind WorkflowClassesController's scoping.</summary>
public class ApplicationScopeRulesTests
{
    [Fact]
    public void UnrestrictedCaller_AllowsAnyResource()
    {
        Assert.True(ApplicationScopeRules.Allows(null, null));
        Assert.True(ApplicationScopeRules.Allows(null, Guid.NewGuid()));
    }

    [Fact]
    public void RestrictedCaller_AllowsOnlyTheSameApplication()
    {
        var applicationId = Guid.NewGuid();
        Assert.True(ApplicationScopeRules.Allows(applicationId, applicationId));
    }

    [Fact]
    public void RestrictedCaller_DeniesADifferentApplication()
    {
        Assert.False(ApplicationScopeRules.Allows(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void RestrictedCaller_DeniesAnUnassignedResource()
    {
        Assert.False(ApplicationScopeRules.Allows(Guid.NewGuid(), null));
    }
}
