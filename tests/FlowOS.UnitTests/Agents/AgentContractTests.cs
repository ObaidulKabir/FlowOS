using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Events;
using FlowOS.Events.Abstractions;
using Xunit;

namespace FlowOS.UnitTests.Agents;

public class AgentContractTests
{
    // 1. Mock Agent Implementation
    private class MockAdvisoryAgent : IAgent
    {
        public Task<AgentResult> ExecuteAsync(AgentContext context)
        {
            // Verify context is accessible
            if (context.Objective == "fail_me")
            {
                return Task.FromResult(AgentResult.Failure("Intentionally failed"));
            }

            // Simulate logic
            var insight = $"Analyzed entity for tenant {context.TenantId}";
            
            return Task.FromResult(AgentResult.FromInsight(insight, new Dictionary<string, object>
            {
                { "confidence", 0.95 }
            }));
        }
    }

    [Fact]
    public async Task Agent_Should_Receive_ReadOnly_Context()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var context = new AgentContext(
            tenantId,
            new { Id = 1, Name = "Test" },
            "Pending",
            new List<IEvent>(),
            "Analyze risk"
        );

        var agent = new MockAdvisoryAgent();

        // Act
        var result = await agent.ExecuteAsync(context);

        // Assert
        Assert.True(result.Success);
        Assert.Contains(tenantId.ToString(), result.Insight);
        Assert.Equal(0.95, result.StructuredData["confidence"]);
    }

    [Fact]
    public void Agent_Result_Should_Map_To_InsightEvent()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var agentId = "agent-001";
        var insight = "Risk is high";
        var objective = "Assess risk";

        // Act
        var evt = new AgentInsightGenerated(tenantId, agentId, insight, objective);

        // Assert
        Assert.Equal(tenantId, evt.TenantId);
        Assert.Equal("AgentInsightGenerated", evt.EventType);
        Assert.Equal(insight, evt.Insight);
        Assert.Equal(objective, evt.ContextObjective);
    }
}
