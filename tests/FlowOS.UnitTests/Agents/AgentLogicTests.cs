using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using Xunit;

namespace FlowOS.UnitTests.Agents
{
    public class AgentLogicTests
    {
        [Fact]
        public async Task RiskAgent_ShouldSuggestEscalate_WhenAmountIsHigh()
        {
            // Arrange
            var agent = new RiskAnalysisAgent();
            var payload = new Dictionary<string, object>
            {
                { "Amount", 6000 },
                { "Category", "Travel" }
            };
            var context = new AgentContext(
                Guid.NewGuid(),
                payload,
                "PendingManager",
                new List<FlowOS.Events.Abstractions.IEvent>(),
                "Analyze Risk"
            );

            // Act
            var result = await agent.ExecuteAsync(context);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("High value", result.Insight);
            Assert.Single(result.SuggestedActions);
            Assert.Equal("EVT-ESCALATE", result.SuggestedActions[0].EventType);
            Assert.Equal(0.95, result.SuggestedActions[0].Confidence);
        }

        [Fact]
        public async Task RiskAgent_ShouldSuggestApprove_WhenOfficeSuppliesLowValue()
        {
            // Arrange
            var agent = new RiskAnalysisAgent();
            var payload = new Dictionary<string, object>
            {
                { "Amount", 50 },
                { "Category", "Office Supplies" }
            };
            var context = new AgentContext(
                Guid.NewGuid(),
                payload,
                "PendingManager",
                new List<FlowOS.Events.Abstractions.IEvent>(),
                "Analyze Risk"
            );

            // Act
            var result = await agent.ExecuteAsync(context);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("Low risk", result.Insight); // Corrected assertion
            Assert.Single(result.SuggestedActions);
            Assert.Equal("EVT-APPROVE", result.SuggestedActions[0].EventType);
        }

        [Fact]
        public async Task RiskAgent_ShouldProvideInsightOnly_WhenStandard()
        {
            // Arrange
            var agent = new RiskAnalysisAgent();
            var payload = new Dictionary<string, object>
            {
                { "Amount", 500 },
                { "Category", "Travel" }
            };
            var context = new AgentContext(
                Guid.NewGuid(),
                payload,
                "PendingManager",
                new List<FlowOS.Events.Abstractions.IEvent>(),
                "Analyze Risk"
            );

            // Act
            var result = await agent.ExecuteAsync(context);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("Standard expense", result.Insight);
            Assert.Empty(result.SuggestedActions); // No action suggested
        }
    }
}
