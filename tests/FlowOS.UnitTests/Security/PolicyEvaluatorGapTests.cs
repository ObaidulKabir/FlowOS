using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.Security.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Security;

/// <summary>
/// Verifies that ConditionJson is preserved from EF entities into the domain Policy
/// and that DefaultPolicyEvaluator honors { "action": "Deny" }.
/// </summary>
public class PolicyEvaluatorGapTests
{
    private FlowOSDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FlowOSDbContext(options);
    }

    [Fact]
    public async Task EfCorePolicyProvider_PreservesConditionJson_WhenMappingToDomainPolicy()
    {
        using var context = GetInMemoryContext();
        var tenantId = Guid.NewGuid();

        var restrictivePolicy = new FlowOS.Security.Models.Policy(
            tenantId,
            "WeekendFreeze",
            "{ \"action\": \"Deny\", \"frozenDays\": [\"Saturday\", \"Sunday\"] }");

        context.Policies.Add(restrictivePolicy);
        await context.SaveChangesAsync();

        var provider = new EfCorePolicyProvider(context);

        var domainPolicies = await provider.GetApplicablePoliciesAsync(new PolicyContext
        {
            TenantId = tenantId.ToString(),
            ActorId = "user-1",
            CommandType = "SomeCommand"
        });

        var mapped = Assert.Single(domainPolicies);
        Assert.Equal("WeekendFreeze", mapped.Name);
        Assert.Contains("frozenDays", mapped.ConditionJson);
        Assert.Contains("Deny", mapped.ConditionJson);
    }

    [Theory]
    [InlineData("WeekendFreeze", "{ \"action\": \"Deny\" }")]
    [InlineData("RestrictedHours", "{ \"action\": \"Deny\", \"reason\": \"outside business hours\" }")]
    public void DefaultPolicyEvaluator_DeniesWhenConditionJsonActionIsDeny(
        string policyName, string conditionJson)
    {
        var evaluator = new DefaultPolicyEvaluator();
        var policy = new Policy(policyName, scope: "Workflow", description: "Database Policy", conditionJson);
        var context = new PolicyContext { TenantId = Guid.NewGuid().ToString(), ActorId = "user-1" };

        var result = evaluator.Evaluate(policy, context);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void DefaultPolicyEvaluator_OnlyDeniesWhenNameIsExactlyDenyAll_OrConditionDeny()
    {
        var evaluator = new DefaultPolicyEvaluator();
        var context = new PolicyContext { TenantId = Guid.NewGuid().ToString(), ActorId = "user-1" };

        var denyAllResult = evaluator.Evaluate(new Policy("DenyAll", "Global", "Blocks everything"), context);
        var anythingElseResult = evaluator.Evaluate(new Policy("denyall", "Global", "Different casing"), context);
        var allowCondition = evaluator.Evaluate(
            new Policy("Allowish", "Global", "ok", "{ \"action\": \"Allow\" }"), context);

        Assert.False(denyAllResult.IsAllowed);
        Assert.True(anythingElseResult.IsAllowed);
        Assert.True(allowCondition.IsAllowed);
    }

    [Fact]
    public void DefaultPolicyEvaluator_DeniesWhenRoleIsDenied()
    {
        var evaluator = new DefaultPolicyEvaluator();
        var context = new PolicyContext
        {
            TenantId = Guid.NewGuid().ToString(),
            ActorId = "user-1",
            Roles = new List<string> { "Guest", "Auditor" }
        };

        var policy = new Policy("NoGuests", "Global", "No guests allowed",
            """{ "deniedRoles": ["Guest", "External"], "reason": "Guests are forbidden" }""");

        var result = evaluator.Evaluate(policy, context);

        Assert.False(result.IsAllowed);
        Assert.Contains("Guests are forbidden", result.Reason);
    }

    [Fact]
    public void DefaultPolicyEvaluator_DeniesWhenRequiredRolesMissing()
    {
        var evaluator = new DefaultPolicyEvaluator();
        var context = new PolicyContext
        {
            TenantId = Guid.NewGuid().ToString(),
            ActorId = "user-1",
            Roles = new List<string> { "User" }
        };

        var policy = new Policy("AdminOnly", "Global", "Admin only",
            """{ "requiredRoles": ["Admin", "SuperAdmin"], "reason": "Requires administrative role" }""");

        var result = evaluator.Evaluate(policy, context);

        Assert.False(result.IsAllowed);
        Assert.Contains("Requires administrative role", result.Reason);
    }

    [Fact]
    public void DefaultPolicyEvaluator_DeniesWhenCommandTypeIsDenied()
    {
        var evaluator = new DefaultPolicyEvaluator();
        var context = new PolicyContext
        {
            TenantId = Guid.NewGuid().ToString(),
            ActorId = "user-1",
            CommandType = "DeleteWorkflowClassCommand"
        };

        var policy = new Policy("NoDelete", "Global", "Deletion protection",
            """{ "deniedCommandTypes": ["DeleteWorkflowClassCommand"], "reason": "Deletion is restricted" }""");

        var result = evaluator.Evaluate(policy, context);

        Assert.False(result.IsAllowed);
        Assert.Contains("Deletion is restricted", result.Reason);
    }
}
