using FlowOS.Application.Common.Interfaces;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public sealed class AgentObservabilityMcpToolsTests
{
    [Fact]
    public void Tools_AreRegisteredWithReadOnlyTenantScopedSchemasAndMetadata()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        using var services = new ServiceCollection().BuildServiceProvider();

        ToolRegistration.RegisterAll(registry, services);

        Assert.True(registry.Contains("get_agent_execution_history"));
        Assert.True(registry.Contains("get_agent_evaluation_metrics"));

        var historySchema = McpToolSchemas.GetAgentExecutionHistory();
        Assert.Equal(
            200,
            historySchema["properties"]!["limit"]!["maximum"]!.Value<int>());
        Assert.False(
            historySchema["additionalProperties"]!.Value<bool>());

        var metricsSchema = McpToolSchemas.GetAgentEvaluationMetrics();
        Assert.Contains(
            metricsSchema["required"]!,
            token => token?.ToString() == "fromUtc");
        Assert.Contains(
            metricsSchema["required"]!,
            token => token?.ToString() == "toUtc");

        var historyProfile = McpToolDescriptions.ProfileFor(
            "get_agent_execution_history");
        Assert.Equal("observability", historyProfile.Category);
        Assert.True(historyProfile.TenantScoped);
        Assert.False(historyProfile.Mutating);
        Assert.Equal("none", historyProfile.SideEffect);

        var metricsProfile = McpToolDescriptions.ProfileFor(
            "get_agent_evaluation_metrics");
        Assert.True(metricsProfile.TenantScoped);
        Assert.False(metricsProfile.Mutating);
    }

    [Fact]
    public async Task History_ResolvesAuthenticatedTenantAndUsesStableSanitizedEnvelope()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var instanceId = Guid.NewGuid();
            var executionId = Guid.NewGuid();
            var service = new StubObservabilityService
            {
                History = new AgentExecutionHistoryDto(
                    Utc(1),
                    Utc(2),
                    50,
                    false,
                    [
                        Execution(executionId, instanceId)
                    ])
            };
            McpRequestContext.TenantId = tenantId;
            McpRequestContext.IsAuthenticatedTransport = true;
            var tools = new AgentObservabilityMcpTools(
                service,
                new FixedTimeProvider(Utc(3)));

            var result = await tools.GetAgentExecutionHistory(
                JObject.FromObject(new
                {
                    workflowInstanceId = instanceId,
                    fromUtc = Utc(1).ToString("O"),
                    toUtc = Utc(2).ToString("O"),
                    limit = 50
                }));

            Assert.False(result.IsError);
            var envelope = JObject.Parse(result.Content.Single().Text);
            Assert.True(envelope["ok"]!.Value<bool>());
            Assert.Equal(
                executionId.ToString(),
                envelope["data"]!["executions"]![0]!["executionId"]?.ToString());
            Assert.Equal(
                "unevaluated",
                envelope["data"]!["executions"]![0]!["outcomeEvaluation"]?.ToString());
            Assert.DoesNotContain(
                "sanitizedFailure",
                envelope.ToString(),
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(tenantId, service.HistoryRequest?.TenantId);
            Assert.Equal(instanceId, service.HistoryRequest?.WorkflowInstanceId);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task Metrics_ReturnStableEnvelope_AndRejectOverlongWindow()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var service = new StubObservabilityService
            {
                Metrics = EmptyMetrics(Utc(1), Utc(2))
            };
            McpRequestContext.TenantId = tenantId;
            McpRequestContext.IsAuthenticatedTransport = true;
            var tools = new AgentObservabilityMcpTools(
                service,
                new FixedTimeProvider(Utc(3)));

            var result = await tools.GetAgentEvaluationMetrics(
                JObject.Parse(
                    """{"fromUtc":"2026-09-01T00:00:00Z","toUtc":"2026-09-02T00:00:00Z"}"""));
            Assert.False(result.IsError);
            var envelope = JObject.Parse(result.Content.Single().Text);
            Assert.True(envelope["ok"]!.Value<bool>());
            Assert.Equal(0, envelope["data"]!["runs"]!.Value<int>());
            Assert.NotNull(envelope["data"]!["confidenceCalibration"]);
            Assert.Equal(tenantId, service.MetricsRequest?.TenantId);

            var invalid = await tools.GetAgentEvaluationMetrics(
                JObject.FromObject(new
                {
                    fromUtc = Utc(1).ToString("O"),
                    toUtc = Utc(1).AddDays(91).ToString("O")
                }));
            Assert.True(invalid.IsError);
            var failure = JObject.Parse(invalid.Content.Single().Text);
            Assert.False(failure["ok"]!.Value<bool>());
            Assert.Equal("MCP-ARG-001", failure["errorCode"]?.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    private static AgentExecutionDto Execution(
        Guid executionId,
        Guid instanceId) =>
        new(
            executionId,
            Guid.NewGuid(),
            instanceId,
            "AgentReview",
            "Agent:test",
            "Live",
            "flowos-hosted",
            "openai",
            "gpt-test",
            "prompt-alias",
            "TenantLlmWorkflowAgent",
            "1",
            null,
            1,
            null,
            null,
            null,
            Utc(1),
            Utc(1).AddMilliseconds(10),
            10,
            "Succeeded",
            true,
            null,
            200,
            1,
            1,
            "APPROVE",
            0.8,
            false,
            true,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            "unevaluated",
            null,
            "Parked");

    private static AgentEvaluationMetricsDto EmptyMetrics(
        DateTime fromUtc,
        DateTime toUtc) =>
        new(
            fromUtc,
            toUtc,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            new AgentLatencyMetricsDto(0, null, null, null),
            new AgentTokenMetricsDto(0, 0),
            new AgentConfidenceCalibrationDto(0, null, Array.Empty<AgentConfidenceBinDto>()),
            Array.Empty<AgentProviderModelMetricsDto>());

    private static DateTime Utc(int day) =>
        new(2026, 9, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class StubObservabilityService : IAgentObservabilityQueryService
    {
        public AgentExecutionHistoryDto History { get; init; } =
            new(Utc(1), Utc(2), 100, false, Array.Empty<AgentExecutionDto>());
        public AgentEvaluationMetricsDto Metrics { get; init; } =
            EmptyMetrics(Utc(1), Utc(2));
        public AgentExecutionHistoryRequest? HistoryRequest { get; private set; }
        public AgentEvaluationMetricsRequest? MetricsRequest { get; private set; }

        public Task<AgentExecutionHistoryDto> GetExecutionHistoryAsync(
            AgentExecutionHistoryRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HistoryRequest = request;
            return Task.FromResult(History);
        }

        public Task<AgentEvaluationMetricsDto> GetEvaluationMetricsAsync(
            AgentEvaluationMetricsRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetricsRequest = request;
            return Task.FromResult(Metrics);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTime now)
        {
            _now = new DateTimeOffset(now);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
