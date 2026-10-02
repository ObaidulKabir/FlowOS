using System.Text.Json;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.Domain.Enums;
using FlowOS.MCP.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Services;

public sealed class DefaultExternalAIAgentExecutor : IExternalAIAgentExecutor
{
    private static readonly string[] SecretKeyFragments =
    {
        "secret",
        "key",
        "password",
        "token",
        "authorization",
        "cookie"
    };

    private readonly IExternalAgentPlanStore _planStore;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<DefaultExternalAIAgentExecutor> _logger;

    public DefaultExternalAIAgentExecutor(
        IExternalAgentPlanStore planStore,
        IToolRegistry toolRegistry,
        ILogger<DefaultExternalAIAgentExecutor> logger)
    {
        _planStore = planStore;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public Task<ExternalAIAgentExecutionResult> ExecutePlanAsync(
        Guid planId,
        Guid? requestedTenantId = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInternalAsync(planId, requestedTenantId, null, cancellationToken);
    }

    public async Task<ExternalAIAgentExecutionResult> ResumePlanAsync(
        Guid planId,
        string? fromStepId = null,
        Guid? requestedTenantId = null,
        CancellationToken cancellationToken = default)
    {
        await _planStore.ResumeFromStepAsync(planId, fromStepId, cancellationToken);
        return await ExecuteInternalAsync(planId, requestedTenantId, fromStepId, cancellationToken);
    }

    private async Task<ExternalAIAgentExecutionResult> ExecuteInternalAsync(
        Guid planId,
        Guid? requestedTenantId,
        string? fromStepId,
        CancellationToken cancellationToken)
    {
        var plan = await _planStore.GetPlanAsync(planId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{planId}' was not found.");

        if (requestedTenantId.HasValue && requestedTenantId.Value != plan.TenantId)
            throw new InvalidOperationException("Requested tenant does not own the plan.");

        var steps = (await _planStore.GetPlanStepsAsync(planId, cancellationToken))
            .OrderBy(step => step.StepIndex)
            .ToList();

        var success = true;
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (step.Status == ExternalAgentStepStatus.Succeeded)
                continue;

            if (!DependenciesSatisfied(step, steps))
            {
                if (step.Status == ExternalAgentStepStatus.Pending)
                {
                    step.MarkSkipped();
                    await _planStore.UpdateStepStatusAsync(planId, step.Id, step, cancellationToken);
                }
                success = false;
                continue;
            }

            var startedAt = DateTime.UtcNow;
            step.MarkRunning(startedAt);
            await _planStore.UpdateStepStatusAsync(planId, step.Id, step, cancellationToken);

            try
            {
                var args = ParseArgs(step.ToolArgsJson, plan.TenantId);
                var result = await _toolRegistry.ExecuteAsync(step.ToolName, args);
                var snapshot = RedactSnapshot(JsonConvert.SerializeObject(result));

                if (result.IsError)
                {
                    step.MarkFailed(DateTime.UtcNow, ExtractErrorMessage(result));
                    success = false;
                }
                else
                {
                    step.MarkSucceeded(DateTime.UtcNow, snapshot);
                }

                await _planStore.MarkStepResultAsync(planId, step.Id, step, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "External agent plan {PlanId} step {StepId} failed.", planId, step.StepId);
                step.MarkFailed(DateTime.UtcNow, ex.Message);
                await _planStore.MarkStepResultAsync(planId, step.Id, step, cancellationToken);
                success = false;
            }
        }

        var refreshed = await _planStore.GetPlanStepsAsync(planId, cancellationToken);
        return new ExternalAIAgentExecutionResult(
            planId,
            plan.TenantId,
            success && refreshed.All(step => step.Status == ExternalAgentStepStatus.Succeeded),
            refreshed
                .OrderBy(step => step.StepIndex)
                .Select(step => new ExternalAIAgentExecutedStep(
                    step.StepId,
                    step.StepIndex,
                    step.ToolName,
                    step.Status.ToString(),
                    step.StartedAtUtc,
                    step.FinishedAtUtc,
                    step.ErrorMessage,
                    step.ResultSnapshotJson))
                .ToList());
    }

    private static bool DependenciesSatisfied(
        ExternalAgentPlanStepRecord step,
        IReadOnlyList<ExternalAgentPlanStepRecord> allSteps)
    {
        var dependencies = ParseDependsOn(step.DependsOnJson);
        if (dependencies.Count == 0)
            return true;

        foreach (var dependency in dependencies)
        {
            var dependencyStep = allSteps.FirstOrDefault(s => string.Equals(s.StepId, dependency, StringComparison.OrdinalIgnoreCase));
            if (dependencyStep == null || dependencyStep.Status != ExternalAgentStepStatus.Succeeded)
                return false;
        }

        return true;
    }

    private static IReadOnlyList<string> ParseDependsOn(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static JObject ParseArgs(string json, Guid tenantId)
    {
        JObject args;
        try
        {
            args = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
        }
        catch
        {
            args = new JObject();
        }

        if (args["tenantId"] == null)
            args["tenantId"] = tenantId.ToString();

        return args;
    }

    private static string ExtractErrorMessage(CallToolResult result)
    {
        if (result.Content.Count == 0 || string.IsNullOrWhiteSpace(result.Content[0].Text))
            return "MCP tool execution failed.";

        try
        {
            var payload = JObject.Parse(result.Content[0].Text!);
            return payload.Value<string>("message") ?? "MCP tool execution failed.";
        }
        catch
        {
            return result.Content[0].Text!;
        }
    }

    private static string RedactSnapshot(string snapshot)
    {
        try
        {
            var token = JToken.Parse(snapshot);
            RedactToken(token);
            return token.ToString(Formatting.None);
        }
        catch
        {
            return snapshot;
        }
    }

    private static void RedactToken(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToList())
            {
                if (SecretKeyFragments.Any(fragment => property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                {
                    property.Value = "[REDACTED]";
                    continue;
                }

                RedactToken(property.Value);
            }
        }
        else if (token is JArray arr)
        {
            foreach (var child in arr)
            {
                RedactToken(child);
            }
        }
        else if (token is JValue value && value.Type == JTokenType.String && value.Value is string text)
        {
            var trimmed = text.Trim();
            if (!LooksLikeJson(trimmed))
                return;

            try
            {
                var nested = JToken.Parse(trimmed);
                RedactToken(nested);
                value.Value = nested.ToString(Formatting.None);
            }
            catch
            {
                // Keep original text when the string is not valid JSON.
            }
        }
    }

    private static bool LooksLikeJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return (value.StartsWith("{", StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal)) ||
               (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal));
    }
}
