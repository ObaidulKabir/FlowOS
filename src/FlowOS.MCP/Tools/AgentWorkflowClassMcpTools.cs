using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Orchestration;
using FlowOS.Application.Handlers.Orchestration;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using MediatR;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

/// <summary>
/// ADR-006/ADR-007: the authoring lifecycle for declarative AgentWorkflowClass plans,
/// exposed over MCP so a coding agent can design, validate and publish a plan without a
/// dashboard.
///
/// These are thin wrappers over the same MediatR commands the REST controller uses, so
/// validation, governance guards and tenant scoping behave identically on both surfaces.
///
/// Phase 4A: authoring only. Nothing here dispatches a plan — the execution engine is
/// Phase 4B, and no tool in this class starts anything.
/// </summary>
public class AgentWorkflowClassMcpTools
{
    private readonly IMediator _mediator;
    private readonly IPluginBindingRegistryService _bindingRegistry;

    public AgentWorkflowClassMcpTools(IMediator mediator, IPluginBindingRegistryService bindingRegistry)
    {
        _mediator = mediator;
        _bindingRegistry = bindingRegistry;
    }

    // ── Design ────────────────────────────────────────────────────────────────

    public async Task<CallToolResult> CreateDraft(JObject args)
    {
        try
        {
            var name = args["name"]?.ToString();
            var version = args["version"]?.ToString() ?? "1.0.0";
            var definitionJson = args["definition"] as JObject;

            if (string.IsNullOrWhiteSpace(name)) return McpToolResults.Fail("MCP-ARG-001", "name is required.");
            if (definitionJson == null) return McpToolResults.Fail("MCP-ARG-001", "definition is required.");

            var definition = definitionJson.ToObject<AgentWorkflowBlueprint>();
            if (definition == null) return McpToolResults.Fail("MCP-ARG-001", "definition format is invalid.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var previousVersionId = ParseOptionalGuid(args["previousVersionId"]);

            var result = await _mediator.Send(new CreateAgentWorkflowClassCommand(
                tenantId, name, version, definition, previousVersionId, args["changeLog"]?.ToString()));

            return McpToolResults.Success(new
            {
                id = result.Id,
                tenantId,
                name = result.Name,
                version = result.Version,
                status = result.Status.ToString(),
                previousVersionId = result.PreviousVersionId,
                message = "Draft agent workflow class created. Validate it, then publish.",
            });
        }
        catch (AgentWorkflowClassValidationException ex)
        {
            return McpToolResults.ValidationFailed(ex.ValidationResult);
        }
        catch (McpToolException ex) { return McpToolResults.Fail(ex.Code, ex.Message); }
        catch (ArgumentException ex) { return McpToolResults.Fail("MCP-ARG-001", ex.Message); }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to create agent workflow draft: {ex.Message}");
        }
    }

    public async Task<CallToolResult> UpdateDraft(JObject args)
    {
        try
        {
            var id = ParseRequiredGuid(args["id"], "id");
            var name = args["name"]?.ToString();
            var version = args["version"]?.ToString() ?? "1.0.0";
            var definitionJson = args["definition"] as JObject;

            if (string.IsNullOrWhiteSpace(name)) return McpToolResults.Fail("MCP-ARG-001", "name is required.");
            if (definitionJson == null) return McpToolResults.Fail("MCP-ARG-001", "definition is required.");

            var definition = definitionJson.ToObject<AgentWorkflowBlueprint>();
            if (definition == null) return McpToolResults.Fail("MCP-ARG-001", "definition format is invalid.");

            var tenantId = McpTenantResolver.ResolveRequired(args);

            var result = await _mediator.Send(new UpdateAgentWorkflowClassDraftCommand(
                tenantId, id, name, version, definition, args["changeLog"]?.ToString()));

            return McpToolResults.Success(new
            {
                id = result.Id,
                status = result.Status.ToString(),
                version = result.Version,
                message = "Draft updated.",
            });
        }
        catch (AgentWorkflowClassValidationException ex)
        {
            return McpToolResults.ValidationFailed(ex.ValidationResult);
        }
        catch (KeyNotFoundException ex) { return McpToolResults.Fail("MCP-NOTFOUND-001", ex.Message); }
        catch (InvalidOperationException ex) { return McpToolResults.Fail("MCP-STATE-001", ex.Message); }
        catch (McpToolException ex) { return McpToolResults.Fail(ex.Code, ex.Message); }
        catch (ArgumentException ex) { return McpToolResults.Fail("MCP-ARG-001", ex.Message); }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to update agent workflow draft: {ex.Message}");
        }
    }

    /// <summary>
    /// Check a blueprint without storing it. An invalid blueprint is a successful call
    /// reporting every problem, so an authoring agent can fix them in one pass instead of
    /// discovering them one publish at a time.
    /// </summary>
    public async Task<CallToolResult> Validate(JObject args)
    {
        try
        {
            var definitionJson = args["definition"] as JObject;
            if (definitionJson == null) return McpToolResults.Fail("MCP-ARG-001", "definition is required.");

            var definition = definitionJson.ToObject<AgentWorkflowBlueprint>();
            if (definition == null) return McpToolResults.Fail("MCP-ARG-001", "definition format is invalid.");

            var validation = await _mediator.Send(new ValidateAgentWorkflowBlueprintCommand(definition));

            return McpToolResults.Success(new
            {
                isValid = validation.IsValid,
                errorCount = validation.Errors.Count,
                errors = validation.Errors.Select(e => new
                {
                    code = e.Code,
                    category = e.Category,
                    message = e.Message,
                    element = e.Element,
                }),
            });
        }
        catch (McpToolException ex) { return McpToolResults.Fail(ex.Code, ex.Message); }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to validate agent workflow blueprint: {ex.Message}");
        }
    }

    // ── Publish ───────────────────────────────────────────────────────────────

    public async Task<CallToolResult> Publish(JObject args)
    {
        try
        {
            var id = ParseRequiredGuid(args["id"], "id");
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var result = await _mediator.Send(new PublishAgentWorkflowClassCommand(tenantId, id));

            return McpToolResults.Success(new
            {
                id = result.Id,
                status = result.Status.ToString(),
                version = result.Version,
                publishedAt = result.PublishedAt,
                message = "Published. The blueprint is now immutable; create a new version to change it.",
            });
        }
        catch (AgentWorkflowClassValidationException ex)
        {
            return McpToolResults.ValidationFailed(ex.ValidationResult);
        }
        catch (KeyNotFoundException ex) { return McpToolResults.Fail("MCP-NOTFOUND-001", ex.Message); }
        catch (InvalidOperationException ex) { return McpToolResults.Fail("MCP-STATE-001", ex.Message); }
        catch (McpToolException ex) { return McpToolResults.Fail(ex.Code, ex.Message); }
        catch (ArgumentException ex) { return McpToolResults.Fail("MCP-ARG-001", ex.Message); }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to publish agent workflow class: {ex.Message}");
        }
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<CallToolResult> Get(JObject args)
    {
        try
        {
            var id = ParseRequiredGuid(args["id"], "id");
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var result = await _mediator.Send(new GetAgentWorkflowClassQuery(tenantId, id));
            if (result == null) return McpToolResults.Fail("MCP-NOTFOUND-001", $"AgentWorkflowClass {id} not found.");

            return McpToolResults.Success(new { tenantId, plan = result });
        }
        catch (McpToolException ex) { return McpToolResults.Fail(ex.Code, ex.Message); }
        catch (ArgumentException ex) { return McpToolResults.Fail("MCP-ARG-001", ex.Message); }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to read agent workflow class: {ex.Message}");
        }
    }

    public async Task<CallToolResult> List(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var skip = args["skip"]?.ToObject<int?>() ?? 0;
            var take = args["take"]?.ToObject<int?>() ?? 50;

            if (skip < 0) return McpToolResults.Fail("MCP-ARG-001", "skip cannot be negative.");
            if (take is < 1 or > 200) return McpToolResults.Fail("MCP-ARG-001", "take must be between 1 and 200.");

            WorkflowClassStatus? status = null;
            var statusText = args["status"]?.ToString();
            if (!string.IsNullOrWhiteSpace(statusText))
            {
                if (!Enum.TryParse<WorkflowClassStatus>(statusText.Trim(), ignoreCase: true, out var parsed) ||
                    !Enum.IsDefined(parsed))
                {
                    return McpToolResults.Fail(
                        "MCP-ARG-001",
                        $"status '{statusText}' is unknown. Allowed values: {string.Join(", ", Enum.GetNames<WorkflowClassStatus>())}.");
                }

                status = parsed;
            }

            var plans = await _mediator.Send(new ListAgentWorkflowClassesQuery(tenantId, status, skip, take));

            return McpToolResults.Success(new
            {
                tenantId,
                count = plans.Count,
                skip,
                take,
                plans = plans.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    version = p.Version,
                    status = p.Status.ToString(),
                    createdAt = p.CreatedAt,
                    publishedAt = p.PublishedAt,
                    objective = p.Definition?.Objective,
                    stepCount = p.Definition?.Steps?.Count ?? 0,
                }),
            });
        }
        catch (McpToolException ex) { return McpToolResults.Fail(ex.Code, ex.Message); }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list agent workflow classes: {ex.Message}");
        }
    }

    public async Task<CallToolResult> GetLineage(JObject args)
    {
        try
        {
            var id = ParseRequiredGuid(args["id"], "id");
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var maxDepth = args["maxDepth"]?.ToObject<int?>() ?? 10;

            if (maxDepth is < 1 or > 100) return McpToolResults.Fail("MCP-ARG-001", "maxDepth must be between 1 and 100.");

            var chain = await _mediator.Send(new GetAgentWorkflowClassLineageQuery(tenantId, id, maxDepth));

            return McpToolResults.Success(new
            {
                tenantId,
                depth = chain.Count,
                lineage = chain.Select(p => new
                {
                    id = p.Id,
                    version = p.Version,
                    status = p.Status.ToString(),
                    publishedAt = p.PublishedAt,
                    changeLog = p.ChangeLog,
                }),
            });
        }
        catch (McpToolException ex) { return McpToolResults.Fail(ex.Code, ex.Message); }
        catch (ArgumentException ex) { return McpToolResults.Fail("MCP-ARG-001", ex.Message); }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to read agent workflow lineage: {ex.Message}");
        }
    }

    // ── Discovery (ADR-007 §9, added by amendment) ──────────────────────────────

    /// <summary>
    /// What the tenant has already declared — tool bindings and LLM/provider bindings —
    /// so a coding agent writing an <see cref="AgentDefinitionBlueprint"/> grounds
    /// <c>requiredToolCapabilities</c> / <c>requiredProviderCapability</c> in what this
    /// tenant can actually satisfy today, instead of inventing tags nothing resolves.
    ///
    /// This is advisory, not enforcement: the blueprint itself stays application-scoped
    /// and portable (ADR-007 §9), so nothing here is written into the plan, and nothing
    /// here blocks publish. A tag this tenant lacks today is still a valid declaration —
    /// readiness is a separate, per-tenant, re-checkable question (ADR-007 §2).
    /// </summary>
    public async Task<CallToolResult> DescribeTenantAgentBindings(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var tools = await _bindingRegistry.ListAgentToolsAsync(tenantId);
            var profiles = await _bindingRegistry.ListAgentProfilesAsync(tenantId);
            var providers = await _bindingRegistry.ListAsync(tenantId, PluginBindingTypes.Agent);
            var defaultProvider = await _bindingRegistry.GetDefaultAgentProviderAsync(tenantId);

            return McpToolResults.Success(new
            {
                tenantId,
                tools = tools.Select(DescribeBinding),
                providers = providers.Select(DescribeBinding),
                profiles = profiles.Select(DescribeBinding),
                defaultProviderAlias = defaultProvider?.SourceName,
                message = tools.Count == 0 && providers.Count == 0
                    ? "This tenant has declared no tool or LLM provider bindings yet. An AgentDefinitionBlueprint " +
                      "may still declare requiredToolCapabilities / requiredProviderCapability — it stays valid and " +
                      "portable — but this tenant will show as not-ready to run it until a matching binding exists."
                    : "Use these aliases' providerName/sourceName as a guide for requiredToolCapabilities and " +
                      "requiredProviderCapability on an AgentDefinitionBlueprint — the tag should describe what the " +
                      "binding actually does, not name the alias itself, so the same definition still resolves if " +
                      "this tenant renames or replaces the binding later.",
            });
        }
        catch (McpToolException ex) { return McpToolResults.Fail(ex.Code, ex.Message); }
        catch (ArgumentException ex) { return McpToolResults.Fail("MCP-ARG-001", ex.Message); }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to describe tenant agent bindings: {ex.Message}");
        }
    }

    private static object DescribeBinding(PluginBindingDto b) => new
    {
        sourceName = b.SourceName,
        providerName = b.ProviderName,
        bindingType = b.BindingType,
        isEnabled = b.IsEnabled,
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Guid ParseRequiredGuid(JToken? token, string argName)
    {
        var text = token?.ToString();
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException($"{argName} is required.");

        if (!Guid.TryParse(text, out var value))
            throw new ArgumentException($"{argName} must be a UUID.");

        return value;
    }

    private static Guid? ParseOptionalGuid(JToken? token)
    {
        var text = token?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (!Guid.TryParse(text, out var value))
            throw new ArgumentException("previousVersionId must be a UUID.");

        return value;
    }
}
