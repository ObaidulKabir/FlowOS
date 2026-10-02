using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.Agents;

public sealed class ExternalAIAgentExecutorTests
{
    [Fact]
    public async Task Executor_Runs_Steps_In_Order()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var plan = ExternalAgentPlanRecord.Create(planId, Guid.NewGuid(), tenantId, "default", DateTime.UtcNow, "[]", 1);
        var steps = new List<ExternalAgentPlanStepRecord>
        {
            ExternalAgentPlanStepRecord.Create(1, planId, "step-1", 0, "tool_one", "{}", null, "[]"),
            ExternalAgentPlanStepRecord.Create(2, planId, "step-2", 1, "tool_two", "{}", null, "[]"),
            ExternalAgentPlanStepRecord.Create(3, planId, "step-3", 2, "tool_three", "{}", null, "[]")
        };

        var store = new InMemoryPlanStore(plan, steps);
        var executionOrder = new List<string>();
        var registry = new Mock<IToolRegistry>();
        registry.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<JObject>()))
            .Returns<string, JObject>((name, _) =>
            {
                executionOrder.Add(name);
                return Task.FromResult(McpToolResults.Success(new { ok = true }));
            });

        var executor = new DefaultExternalAIAgentExecutor(store, registry.Object, NullLogger<DefaultExternalAIAgentExecutor>.Instance);

        var result = await executor.ExecutePlanAsync(planId, tenantId);

        Assert.True(result.Success);
        Assert.Equal(new[] { "tool_one", "tool_two", "tool_three" }, executionOrder);
        Assert.All(result.StepResults, step => Assert.Equal("Succeeded", step.Status));
    }

    [Fact]
    public async Task Executor_Skips_Dependents_And_Redacts_Secrets()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var plan = ExternalAgentPlanRecord.Create(planId, Guid.NewGuid(), tenantId, "default", DateTime.UtcNow, "[]", 1);
        var steps = new List<ExternalAgentPlanStepRecord>
        {
            ExternalAgentPlanStepRecord.Create(1, planId, "step-1", 0, "tool_success", "{}", null, "[]"),
            ExternalAgentPlanStepRecord.Create(2, planId, "step-2", 1, "tool_fail", "{}", null, "[]"),
            ExternalAgentPlanStepRecord.Create(3, planId, "step-3", 2, "tool_never", "{}", null, "[\"step-2\"]")
        };

        var store = new InMemoryPlanStore(plan, steps);
        var registry = new Mock<IToolRegistry>();
        registry.Setup(x => x.ExecuteAsync("tool_success", It.IsAny<JObject>()))
            .ReturnsAsync(McpToolResults.Success(new { apiKey = "secret123", password = "x", tenantId }));
        registry.Setup(x => x.ExecuteAsync("tool_fail", It.IsAny<JObject>()))
            .ReturnsAsync(new CallToolResult
            {
                IsError = true,
                Content =
                [
                    new ToolContent
                    {
                        Type = "text",
                        Text = "{\"message\":\"boom\"}"
                    }
                ]
            });

        var executor = new DefaultExternalAIAgentExecutor(store, registry.Object, NullLogger<DefaultExternalAIAgentExecutor>.Instance);

        var result = await executor.ExecutePlanAsync(planId, tenantId);

        Assert.False(result.Success);
        Assert.Equal("Succeeded", result.StepResults[0].Status);
        Assert.Equal("Failed", result.StepResults[1].Status);
        Assert.Equal("Skipped", result.StepResults[2].Status);
        Assert.DoesNotContain("secret123", result.StepResults[0].ResultSnapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"x\"", result.StepResults[0].ResultSnapshotJson, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.StepResults[0].ResultSnapshotJson, StringComparison.Ordinal);
        registry.Verify(x => x.ExecuteAsync("tool_never", It.IsAny<JObject>()), Times.Never);
    }

    private sealed class InMemoryPlanStore : IExternalAgentPlanStore
    {
        private readonly ExternalAgentPlanRecord _plan;
        private readonly List<ExternalAgentPlanStepRecord> _steps;

        public InMemoryPlanStore(ExternalAgentPlanRecord plan, List<ExternalAgentPlanStepRecord> steps)
        {
            _plan = plan;
            _steps = steps;
        }

        public Task<Guid> CreatePlanAsync(ExternalAgentPlanRecord plan, IReadOnlyList<ExternalAgentPlanStepRecord> steps, CancellationToken ct = default)
            => Task.FromResult(plan.Id);

        public Task<ExternalAgentPlanRecord?> GetLatestByChangeAsync(Guid changeId, CancellationToken ct = default)
            => Task.FromResult(changeId == _plan.ChangeId ? _plan : null);

        public Task<ExternalAgentPlanRecord?> GetPlanAsync(Guid planId, CancellationToken ct = default)
            => Task.FromResult(planId == _plan.Id ? _plan : null);

        public Task<IReadOnlyList<ExternalAgentPlanStepRecord>> GetPlanStepsAsync(Guid planId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExternalAgentPlanStepRecord>>(_steps.OrderBy(s => s.StepIndex).ToList());

        public Task UpdateStepStatusAsync(Guid planId, long stepId, ExternalAgentPlanStepRecord step, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task MarkStepResultAsync(Guid planId, long stepId, ExternalAgentPlanStepRecord step, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ResumeFromStepAsync(Guid planId, string? fromStepId, CancellationToken ct = default)
        {
            var startIndex = 0;
            if (!string.IsNullOrWhiteSpace(fromStepId))
            {
                var matched = _steps.FirstOrDefault(s => s.StepId == fromStepId);
                if (matched != null)
                    startIndex = matched.StepIndex;
            }

            foreach (var step in _steps.Where(s => s.StepIndex >= startIndex))
            {
                if (step.Status is FlowOS.Domain.Enums.ExternalAgentStepStatus.Failed or FlowOS.Domain.Enums.ExternalAgentStepStatus.Skipped)
                    step.ResetToPending();
            }

            return Task.CompletedTask;
        }
    }
}
