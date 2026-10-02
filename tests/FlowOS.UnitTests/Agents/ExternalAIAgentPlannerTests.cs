using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations.Adapters;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.Agents;

public sealed class ExternalAIAgentPlannerTests
{
    [Fact]
    public async Task Planner_Drops_Unknown_Tools_And_Persists_Valid_Steps()
    {
        var tenantId = Guid.NewGuid();
        var change = ExternalAgentChangeRecord.Create(Guid.NewGuid(), tenantId, Guid.NewGuid(), "InvoiceOverdue", "{\"invoiceId\":\"INV-1\"}", DateTime.UtcNow);
        var capturedSteps = new List<ExternalAgentPlanStepRecord>();
        ExternalAgentPlanRecord? capturedPlan = null;

        var changeStore = new Mock<IExternalAgentChangeStore>();
        changeStore.Setup(x => x.GetByIdAsync(change.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(change);

        var planStore = new Mock<IExternalAgentPlanStore>();
        planStore.Setup(x => x.CreatePlanAsync(It.IsAny<ExternalAgentPlanRecord>(), It.IsAny<IReadOnlyList<ExternalAgentPlanStepRecord>>(), It.IsAny<CancellationToken>()))
            .Returns<ExternalAgentPlanRecord, IReadOnlyList<ExternalAgentPlanStepRecord>, CancellationToken>((plan, steps, _) =>
            {
                capturedPlan = plan;
                capturedSteps = steps.ToList();
                return Task.FromResult(plan.Id);
            });

        var bindings = new Mock<IPluginBindingRegistryService>();
        bindings.Setup(x => x.GetDefaultAgentProviderAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PluginBindingDto?)null);

        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(x => x.GetTools()).Returns(new[]
        {
            new McpTool { Name = "publish_event", Description = "Publish a workflow event.", InputSchema = new { } },
            new McpTool { Name = "get_external_agent_change", Description = "Read a change.", InputSchema = new { } }
        });
        toolRegistry.Setup(x => x.Contains("publish_event")).Returns(true);
        toolRegistry.Setup(x => x.Contains("get_external_agent_change")).Returns(true);
        toolRegistry.Setup(x => x.Contains("unknown_tool")).Returns(false);

        var planner = new DefaultExternalAIAgentPlanner(
            changeStore.Object,
            planStore.Object,
            bindings.Object,
            new LlmProviderAdapterRegistry(new ILlmProviderAdapterFactory[] { new OpenAiAdapterFactory() }),
            new FakeTransport("""
                [
                  {"stepId":"step-1","toolName":"publish_event","toolArgs":{"workflowInstanceId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","eventType":"REMIND"},"description":"Publish reminder","dependsOn":[]},
                  {"stepId":"step-2","toolName":"unknown_tool","toolArgs":{},"description":"Ignore me","dependsOn":[]}
                ]
                """),
            toolRegistry.Object,
            NullLogger<DefaultExternalAIAgentPlanner>.Instance,
            new FakeHostedRuntime());

        var result = await planner.PlanAsync(change.Id, tenantId, "ops-planner");

        Assert.Single(result.ValidationDropped);
        Assert.Equal("unknown_tool", result.ValidationDropped[0]);
        Assert.Single(result.Steps);
        Assert.Equal("publish_event", result.Steps[0].ToolName);
        Assert.NotNull(capturedPlan);
        Assert.Single(capturedSteps);
        Assert.Contains(tenantId.ToString(), capturedSteps[0].ToolArgsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Planner_Falls_Back_To_Inspection_Step_When_Model_Output_Is_Invalid()
    {
        var tenantId = Guid.NewGuid();
        var change = ExternalAgentChangeRecord.Create(Guid.NewGuid(), tenantId, Guid.NewGuid(), "InvoiceOverdue", "{\"invoiceId\":\"INV-2\"}", DateTime.UtcNow);

        var changeStore = new Mock<IExternalAgentChangeStore>();
        changeStore.Setup(x => x.GetByIdAsync(change.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(change);

        var planStore = new Mock<IExternalAgentPlanStore>();
        planStore.Setup(x => x.CreatePlanAsync(It.IsAny<ExternalAgentPlanRecord>(), It.IsAny<IReadOnlyList<ExternalAgentPlanStepRecord>>(), It.IsAny<CancellationToken>()))
            .Returns<ExternalAgentPlanRecord, IReadOnlyList<ExternalAgentPlanStepRecord>, CancellationToken>((plan, _, _) => Task.FromResult(plan.Id));

        var bindings = new Mock<IPluginBindingRegistryService>();
        bindings.Setup(x => x.GetDefaultAgentProviderAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PluginBindingDto?)null);

        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(x => x.GetTools()).Returns(new[]
        {
            new McpTool { Name = "get_external_agent_change", Description = "Read a change.", InputSchema = new { } }
        });
        toolRegistry.Setup(x => x.Contains("get_external_agent_change")).Returns(true);

        var planner = new DefaultExternalAIAgentPlanner(
            changeStore.Object,
            planStore.Object,
            bindings.Object,
            new LlmProviderAdapterRegistry(new ILlmProviderAdapterFactory[] { new OpenAiAdapterFactory() }),
            new FakeTransport("not-json", "still-not-json"),
            toolRegistry.Object,
            NullLogger<DefaultExternalAIAgentPlanner>.Instance,
            new FakeHostedRuntime());

        var result = await planner.PlanAsync(change.Id, tenantId);

        Assert.Single(result.Steps);
        Assert.Equal("get_external_agent_change", result.Steps[0].ToolName);
    }

    [Fact]
    public void ToolRegistration_Registers_External_Agent_Plan_Tools()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var registry = new ToolRegistry(new NullLogger<ToolRegistry>(), provider.GetRequiredService<IServiceScopeFactory>());

        ToolRegistration.RegisterAll(registry, provider);

        Assert.True(registry.Contains("plan_for_change"));
        Assert.True(registry.Contains("execute_plan"));
        Assert.True(registry.Contains("resume_plan"));
        Assert.True(registry.Contains("get_external_agent_plan"));
    }

    private sealed class FakeTransport : ILlmTransport
    {
        private readonly Queue<string> _responses;

        public FakeTransport(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public Task<LlmTransportResult> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            var content = _responses.Count > 0 ? _responses.Dequeue() : "[]";
            return Task.FromResult(new LlmTransportResult(true, content, 200, "req-1", null));
        }
    }

    private sealed class FakeHostedRuntime : IFlowOsHostedLlmRuntime
    {
        public bool IsConfigured => true;

        public FlowOsHostedLlmPublicSettings PublicSettings => new(true, true, "openai", "gpt-4o-mini", null, 1000);

        public Task<FlowOsHostedLlmLease> TryLeaseAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FlowOsHostedLlmLease(true, "test-api-key", "gpt-4o-mini", "https://api.openai.com/v1/chat/completions", null, null, tenantId));
        }
    }
}
