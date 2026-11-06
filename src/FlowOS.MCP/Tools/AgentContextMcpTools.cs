using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Core.Interfaces;
using FlowOS.Core.Security;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.Security.Interfaces;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class AgentContextMcpTools
{
    private readonly IDecisionPacketBuilder _packetBuilder;
    private readonly IPluginBindingRegistryService _pluginBindings;
    private readonly IAgentToolHost? _toolHost;
    private readonly bool _allowGatewayPrefetch;
    private readonly IConfiguration? _configuration;
    private readonly ICurrentUser? _currentUser;
    private readonly ICapabilityService? _capabilityService;

    public AgentContextMcpTools(
        IDecisionPacketBuilder packetBuilder,
        IPluginBindingRegistryService pluginBindings,
        IAgentToolHost? toolHost = null,
        IConfiguration? configuration = null,
        ICurrentUser? currentUser = null,
        ICapabilityService? capabilityService = null)
    {
        _packetBuilder = packetBuilder;
        _pluginBindings = pluginBindings;
        _toolHost = toolHost;
        _configuration = configuration;
        _allowGatewayPrefetch = configuration?.GetValue<bool?>(
            "FlowOS:Agents:AllowGatewayPrefetch") ?? true;
        _currentUser = currentUser;
        _capabilityService = capabilityService;
    }

    /// <summary>
    /// Gate for any write that can point a tenant's BYO LLM key at a different endpoint or remove a
    /// provider outright (<see cref="AgentProviderKinds.ManageCapability"/>). Returns null when allowed.
    ///
    /// <paramref name="activity"/>-only instances constructed without a current-user/capability-service
    /// (always the case in production - both are registered in DI and <c>ToolRegistration</c> resolves
    /// this class through it) skip the check: that happens only in unit tests that construct this class
    /// directly to exercise unrelated logic, never in a live deployment.
    /// </summary>
    private async Task<CallToolResult?> RequireAgentManageCapabilityAsync(Guid tenantId, string activity)
    {
        if (_currentUser == null || _capabilityService == null)
            return null;

        if (await TenantCapabilityGate.HasCapabilityAsync(
                _currentUser, _capabilityService, tenantId, AgentProviderKinds.ManageCapability))
        {
            return null;
        }

        return McpToolResults.Fail(
            "MCP-AUTHZ-001",
            $"Missing required capability: {AgentProviderKinds.ManageCapability}",
            new
            {
                activity,
                tenantId,
                requiredAnyOf = new[] { AgentProviderKinds.ManageCapability },
                callerRoles = _currentUser.Roles,
                callerScopes = _currentUser.Scopes
            });
    }

    private bool IsEndpointSchemeAllowed(string scheme)
    {
        if (string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return false;

        var environmentName = _configuration?["ASPNETCORE_ENVIRONMENT"];
        return TenantIdentityRules.AllowInsecureAgentEndpoints(
            environmentName,
            _configuration?[TenantIdentityRules.AllowInsecureAgentEndpointsKey]);
    }

    public async Task<CallToolResult> GetAgentContext(JObject args)
    {
        try
        {
            var instanceIdStr = args["workflowInstanceId"]?.ToString();
            if (string.IsNullOrWhiteSpace(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var instanceId))
                return McpToolResults.Fail("MCP-ARG-002", "workflowInstanceId must be a valid UUID.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var packet = await _packetBuilder.BuildAsync(
                tenantId,
                instanceId,
                args["objective"]?.ToString());

            if (packet == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", "Workflow instance was not found.");

            var prefetch = args["prefetch"]?.Value<bool>() ?? _allowGatewayPrefetch;
            if (prefetch && !_allowGatewayPrefetch)
                return McpToolResults.Fail("MCP-ARG-001", "Gateway prefetch is disabled; request context with prefetch:false.");
            var prefetched = false;
            if (prefetch && _toolHost != null)
            {
                packet = await _toolHost.PrefetchAsync(packet);
                prefetched = true;
            }

            return McpToolResults.Success(new
            {
                agentContext = AgentContextComposer.FromPacket(packet, "live", prefetched: prefetched),
                ranAgent = false
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to build Agent Context.");
        }
    }

    public async Task<CallToolResult> PreviewAgentContext(JObject args)
    {
        try
        {
            var classIdStr = args["workflowClassId"]?.ToString();
            var stepId = args["stepId"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(classIdStr) || !Guid.TryParse(classIdStr, out var classId))
                return McpToolResults.Fail("MCP-ARG-002", "workflowClassId must be a valid UUID.");
            if (string.IsNullOrWhiteSpace(stepId))
                return McpToolResults.Fail("MCP-ARG-001", "stepId is required.");

            Guid? bindingId = null;
            var bindingIdStr = args["contextBindingId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(bindingIdStr))
            {
                if (!Guid.TryParse(bindingIdStr, out var parsedBinding))
                    return McpToolResults.Fail("MCP-ARG-002", "contextBindingId must be a valid UUID.");
                bindingId = parsedBinding;
            }

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var packet = await _packetBuilder.PreviewAsync(
                tenantId,
                classId,
                stepId,
                bindingId,
                ParseCanonical(args["canonicalContext"]),
                args["currentState"]?.ToString(),
                args["objective"]?.ToString());

            if (packet == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", "Workflow class or context binding was not found.");

            var prefetch = args["prefetch"]?.Value<bool>() ?? false;
            if (prefetch && !_allowGatewayPrefetch)
                return McpToolResults.Fail("MCP-ARG-001", "Gateway prefetch is disabled; request context with prefetch:false.");
            var prefetched = false;
            if (prefetch && _toolHost != null)
            {
                packet = await _toolHost.PrefetchAsync(packet);
                prefetched = true;
            }

            return McpToolResults.Success(new
            {
                agentContext = AgentContextComposer.FromPacket(
                    packet,
                    "preview",
                    workflowClassId: classId,
                    prefetched: prefetched,
                    hideInstance: true),
                ranAgent = false
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to preview Agent Context.");
        }
    }

    public async Task<CallToolResult> UpsertAgentPrompt(JObject args)
    {
        try
        {
            var alias = FirstNonEmpty(args, "alias", "sourceName");
            if (string.IsNullOrWhiteSpace(alias))
                return McpToolResults.Fail("MCP-ARG-001", "alias is required.");

            var kind = args["kind"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(kind))
                kind = AgentPromptKinds.Markdown;
            if (!AgentPromptKinds.IsKnown(kind))
            {
                return McpToolResults.Fail(
                    "PLUGIN-BIND-005",
                    "Unknown prompt kind. Use markdown or flowos-prompt.");
            }

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var configuration = BuildPromptConfiguration(args);
            var parsed = AgentPromptConfiguration.Parse(configuration);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Body))
            {
                var existing = (await _pluginBindings.ListAsync(tenantId, PluginBindingTypes.Prompt, alias))
                    .FirstOrDefault();
                var existingPrompt = existing?.Configuration as AgentPromptConfiguration;
                if (existingPrompt == null || string.IsNullOrWhiteSpace(existingPrompt.Body))
                {
                    return McpToolResults.Fail(
                        "PLUGIN-BIND-005",
                        "instructions (or text) is required when creating a prompt.");
                }
            }

            var isEnabled = args["isEnabled"]?.Value<bool>() ?? true;
            var binding = await _pluginBindings.UpsertAsync(
                tenantId,
                PluginBindingTypes.Prompt,
                alias,
                kind,
                isEnabled,
                configuration);

            return McpToolResults.Success(ToPromptDto(binding));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to save agent prompt.");
        }
    }

    public async Task<CallToolResult> ListAgentPrompts(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            bool? enabledOnly = args["enabledOnly"]?.Value<bool>();
            var bindings = await _pluginBindings.ListAsync(
                tenantId,
                PluginBindingTypes.Prompt,
                args["alias"]?.ToString()?.Trim(),
                enabledOnly);

            return McpToolResults.Success(new
            {
                totalCount = bindings.Count,
                prompts = bindings.Select(ToPromptDto).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to list agent prompts.");
        }
    }

    public async Task<CallToolResult> GetAgentPrompt(JObject args)
    {
        try
        {
            var alias = FirstNonEmpty(args, "alias", "sourceName");
            if (string.IsNullOrWhiteSpace(alias))
                return McpToolResults.Fail("MCP-ARG-001", "alias is required.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var binding = await _pluginBindings.GetEnabledAsync(tenantId, PluginBindingTypes.Prompt, alias);
            if (binding == null)
            {
                var listed = await _pluginBindings.ListAsync(tenantId, PluginBindingTypes.Prompt, alias);
                binding = listed.FirstOrDefault();
            }

            if (binding == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Agent prompt '{alias}' was not found.");

            return McpToolResults.Success(ToPromptDto(binding));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to load agent prompt.");
        }
    }

    public async Task<CallToolResult> UpsertAgentProvider(JObject args)
    {
        try
        {
            var alias = FirstNonEmpty(args, "alias", "sourceName");
            if (string.IsNullOrWhiteSpace(alias))
                return McpToolResults.Fail("MCP-ARG-001", "alias is required.");

            var tenantId = McpTenantResolver.ResolveRequired(args);

            var denied = await RequireAgentManageCapabilityAsync(tenantId, "upsert_agent_provider");
            if (denied != null)
                return denied;

            var existing = (await _pluginBindings.ListAsync(tenantId, PluginBindingTypes.Agent, alias))
                .FirstOrDefault();

            var providerName = FirstNonEmpty(args, "providerName") ?? existing?.ProviderName;
            if (string.IsNullOrWhiteSpace(providerName))
            {
                return McpToolResults.Fail(
                    "MCP-ARG-001",
                    "providerName is required when creating a provider (openai, anthropic, azure-openai, google, custom, flowos-risk).");
            }

            if (!AgentProviderKinds.IsKnown(providerName))
            {
                return McpToolResults.Fail(
                    "PLUGIN-BIND-005",
                    "Unknown providerName. Use openai, anthropic, azure-openai, google, custom, flowos-risk, or flowos-hosted.");
            }

            if (AgentProviderKinds.IsReservedAlias(alias) && !AgentProviderKinds.IsFlowOsHosted(providerName))
            {
                return McpToolResults.Fail(
                    "AGENT-ALIAS-RESERVED",
                    "The alias 'flowos-hosted' is platform-reserved: a step whose agentProvider resolves to it always gets the platform's hosted lease at runtime, regardless of what is saved here. Rejected to prevent silently orphaned configuration - use a different alias for a BYO providerName key.");
            }

            if (!AgentProviderKinds.IsFlowOsHosted(providerName))
            {
                var endpointCandidate = FirstNonEmpty(args, "endpoint")
                    ?? (args["configuration"] as JObject)?["endpoint"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(endpointCandidate))
                {
                    if (!IsWellFormedHttpUrl(endpointCandidate))
                    {
                        return McpToolResults.Fail(
                            "MCP-ARG-001",
                            "endpoint must be a full http(s) URL (e.g. https://api.openai.com/v1/chat/completions), not a bare host, path, or email address.");
                    }

                    var endpointScheme = new Uri(endpointCandidate, UriKind.Absolute).Scheme;
                    if (!IsEndpointSchemeAllowed(endpointScheme))
                    {
                        return McpToolResults.Fail(
                            "MCP-ARG-001",
                            "endpoint must be https:// outside Development - a BYO API key is sent to it as a Bearer token.");
                    }
                }
            }

            var isEnabled = args["isEnabled"]?.Value<bool>() ?? existing?.IsEnabled ?? true;
            var configuration = AgentProviderKinds.IsFlowOsHosted(providerName)
                ? "{}"
                : BuildProviderConfiguration(args);
            var binding = await _pluginBindings.UpsertAsync(
                tenantId,
                PluginBindingTypes.Agent,
                alias,
                providerName,
                isEnabled,
                configuration);

            return McpToolResults.Success(ToProviderDto(binding));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to save agent provider.");
        }
    }

    public async Task<CallToolResult> ListAgentProviders(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            bool? enabledOnly = args["enabledOnly"]?.Value<bool>();
            var bindings = await _pluginBindings.ListAsync(
                tenantId,
                PluginBindingTypes.Agent,
                args["alias"]?.ToString()?.Trim(),
                enabledOnly);

            return McpToolResults.Success(new
            {
                totalCount = bindings.Count,
                providers = bindings.Select(ToProviderDto).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to list agent providers.");
        }
    }

    public async Task<CallToolResult> GetAgentProvider(JObject args)
    {
        try
        {
            var alias = FirstNonEmpty(args, "alias", "sourceName");
            if (string.IsNullOrWhiteSpace(alias))
                return McpToolResults.Fail("MCP-ARG-001", "alias is required.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var binding = await _pluginBindings.GetEnabledAsync(tenantId, PluginBindingTypes.Agent, alias);
            if (binding == null)
            {
                var listed = await _pluginBindings.ListAsync(tenantId, PluginBindingTypes.Agent, alias);
                binding = listed.FirstOrDefault();
            }

            if (binding == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Agent provider '{alias}' was not found.");

            return McpToolResults.Success(ToProviderDto(binding));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to load agent provider.");
        }
    }

    public async Task<CallToolResult> DeleteAgentProvider(JObject args)
    {
        try
        {
            var alias = FirstNonEmpty(args, "alias", "sourceName");
            if (string.IsNullOrWhiteSpace(alias))
                return McpToolResults.Fail("MCP-ARG-001", "alias is required.");

            var tenantId = McpTenantResolver.ResolveRequired(args);

            var denied = await RequireAgentManageCapabilityAsync(tenantId, "delete_agent_provider");
            if (denied != null)
                return denied;

            var profile = McpToolDescriptions.ProfileFor("delete_agent_provider");
            if (profile.RequiresHumanConfirmation)
            {
                var confirmed = args["confirmHumanApproval"]?.Value<bool>() ?? false;
                if (!confirmed)
                {
                    return McpToolResults.Fail(
                        "MCP-APPROVAL-REQUIRED",
                        "High-risk irreversible operation 'delete_agent_provider' requires explicit human confirmation. Supply 'confirmHumanApproval: true' in arguments to execute.");
                }
            }

            var deleted = await _pluginBindings.DeleteAsync(tenantId, PluginBindingTypes.Agent, alias);
            if (!deleted)
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Agent provider '{alias}' was not found.");

            return McpToolResults.Success(new { alias, tenantId, status = "Deleted", message = "Agent provider permanently deleted." });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to delete agent provider.");
        }
    }

    public async Task<CallToolResult> BackfillAgentProviderKeyMetadata(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var denied = await RequireAgentManageCapabilityAsync(tenantId, "backfill_agent_provider_key_metadata");
            if (denied != null)
                return denied;

            var updatedCount = await _pluginBindings.BackfillAgentProviderKeyMetadataAsync(tenantId);
            return McpToolResults.Success(new
            {
                tenantId,
                providersBackfilled = updatedCount,
                message = updatedCount > 0
                    ? $"Computed apiKeyFingerprint/keyUpdatedAtUtc for {updatedCount} provider(s) that predate those fields. The key itself was never exposed or changed."
                    : "No providers needed backfilling - every stored key already has a fingerprint, or no agent providers have a key stored."
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to backfill agent provider key metadata.");
        }
    }

    public async Task<CallToolResult> UpsertAgentTool(JObject args)
    {
        try
        {
            var alias = FirstNonEmpty(args, "alias", "sourceName");
            if (string.IsNullOrWhiteSpace(alias))
                return McpToolResults.Fail("MCP-ARG-001", "alias is required.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var existing = await _pluginBindings.GetAgentToolAsync(tenantId, alias);

            var providerName = FirstNonEmpty(args, "providerName") ?? existing?.ProviderName;
            if (string.IsNullOrWhiteSpace(providerName))
            {
                return McpToolResults.Fail(
                    "MCP-ARG-001",
                    "providerName is required when creating a tool (e.g. LookupRecord, QueryRecords, FetchDocument, SearchKnowledge, CheckPolicy, connector:*).");
            }

            var sideEffect = FirstNonEmpty(args, "sideEffect")?.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(sideEffect) &&
                sideEffect is not ("none" or "read" or "write" or "notify"))
            {
                return McpToolResults.Fail("MCP-ARG-001", "sideEffect must be one of 'none', 'read', 'write', or 'notify'.");
            }

            var prefetch = args["prefetch"]?.Value<bool>() ?? false;
            if (prefetch && sideEffect is "write" or "notify")
            {
                return McpToolResults.Fail("MCP-ARG-001", "Tools with sideEffect 'write' or 'notify' cannot be configured for prefetch.");
            }

            var isEnabled = args["isEnabled"]?.Value<bool>() ?? existing?.IsEnabled ?? true;
            var configuration = BuildToolConfiguration(args);
            var binding = await _pluginBindings.UpsertAsync(
                tenantId,
                PluginBindingTypes.Action,
                alias,
                providerName,
                isEnabled,
                configuration);

            return McpToolResults.Success(ToToolDto(binding));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to save agent tool.");
        }
    }

    public async Task<CallToolResult> ListAgentTools(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            bool? enabledOnly = args["enabledOnly"]?.Value<bool>();
            var bindings = await _pluginBindings.ListAsync(
                tenantId,
                PluginBindingTypes.Action,
                args["alias"]?.ToString()?.Trim(),
                enabledOnly);

            return McpToolResults.Success(new
            {
                totalCount = bindings.Count,
                tools = bindings.Select(ToToolDto).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to list agent tools.");
        }
    }

    public async Task<CallToolResult> GetAgentTool(JObject args)
    {
        try
        {
            var alias = FirstNonEmpty(args, "alias", "sourceName");
            if (string.IsNullOrWhiteSpace(alias))
                return McpToolResults.Fail("MCP-ARG-001", "alias is required.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var binding = await _pluginBindings.GetAgentToolAsync(tenantId, alias);
            if (binding == null)
            {
                var listed = await _pluginBindings.ListAsync(tenantId, PluginBindingTypes.Action, alias);
                binding = listed.FirstOrDefault();
            }

            if (binding == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Agent tool '{alias}' was not found.");

            return McpToolResults.Success(ToToolDto(binding));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to load agent tool.");
        }
    }

    public async Task<CallToolResult> UpsertAgentProfile(JObject args)
    {
        try
        {
            var alias = FirstNonEmpty(args, "alias", "sourceName");
            if (string.IsNullOrWhiteSpace(alias))
                return McpToolResults.Fail("MCP-ARG-001", "alias is required.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var existing = await _pluginBindings.GetAgentProfileAsync(tenantId, alias);

            var role = FirstNonEmpty(args, "role") ?? existing?.ProviderName;
            if (string.IsNullOrWhiteSpace(role))
            {
                return McpToolResults.Fail("MCP-ARG-001", "role is required when creating an agent profile (e.g. LoanOfficer, TriageSpecialist).");
            }

            var isEnabled = args["isEnabled"]?.Value<bool>() ?? existing?.IsEnabled ?? true;
            var configuration = BuildProfileConfiguration(args, role);
            var binding = await _pluginBindings.UpsertAsync(
                tenantId,
                PluginBindingTypes.Profile,
                alias,
                role,
                isEnabled,
                configuration);

            return McpToolResults.Success(ToProfileDto(binding));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to save agent profile.");
        }
    }

    public async Task<CallToolResult> ListAgentProfiles(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            bool? enabledOnly = args["enabledOnly"]?.Value<bool>();
            var bindings = await _pluginBindings.ListAsync(
                tenantId,
                PluginBindingTypes.Profile,
                args["alias"]?.ToString()?.Trim(),
                enabledOnly);

            return McpToolResults.Success(new
            {
                totalCount = bindings.Count,
                profiles = bindings.Select(ToProfileDto).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to list agent profiles.");
        }
    }

    public async Task<CallToolResult> GetAgentProfile(JObject args)
    {
        try
        {
            var alias = FirstNonEmpty(args, "alias", "sourceName");
            if (string.IsNullOrWhiteSpace(alias))
                return McpToolResults.Fail("MCP-ARG-001", "alias is required.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var binding = await _pluginBindings.GetAgentProfileAsync(tenantId, alias);
            if (binding == null)
            {
                var listed = await _pluginBindings.ListAsync(tenantId, PluginBindingTypes.Profile, alias);
                binding = listed.FirstOrDefault();
            }

            if (binding == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Agent profile '{alias}' was not found.");

            return McpToolResults.Success(ToProfileDto(binding));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to load agent profile.");
        }
    }

    private static object ToToolDto(PluginBindingDto binding)
    {
        var settings = binding.Configuration as AgentToolPublicSettings
            ?? AgentToolConfiguration.Public(null);
        return new
        {
            alias = binding.SourceName,
            providerName = binding.ProviderName,
            binding.IsEnabled,
            description = settings.Description,
            sideEffect = settings.SideEffect,
            prefetch = settings.Prefetch,
            requiredCapability = settings.RequiredCapability,
            parametersSchema = settings.ParametersSchema,
            argumentMapping = settings.ArgumentMapping,
            binding.Id,
            binding.TenantId,
            binding.CreatedAtUtc,
            binding.UpdatedAtUtc
        };
    }

    private static object ToProfileDto(PluginBindingDto binding)
    {
        var settings = binding.Configuration as AgentProfilePublicSettings
            ?? AgentProfileConfiguration.Public(null);
        return new
        {
            alias = binding.SourceName,
            role = settings.Role ?? binding.ProviderName,
            binding.IsEnabled,
            description = settings.Description,
            providerAlias = settings.ProviderAlias,
            promptAlias = settings.PromptAlias,
            toolAliases = settings.ToolAliases,
            autoCommitThreshold = settings.AutoCommitThreshold,
            allowedEvents = settings.AllowedEvents,
            binding.Id,
            binding.TenantId,
            binding.CreatedAtUtc,
            binding.UpdatedAtUtc
        };
    }

    private static string BuildToolConfiguration(JObject args)
    {
        var nested = args["configuration"] as JObject;
        var description = FirstNonEmpty(args, "description") ?? nested?["description"]?.ToString();
        var sideEffect = FirstNonEmpty(args, "sideEffect") ?? nested?["sideEffect"]?.ToString() ?? "none";
        var requiredCapability = FirstNonEmpty(args, "requiredCapability") ?? nested?["requiredCapability"]?.ToString();
        var prefetch = args["prefetch"]?.Value<bool>() ?? nested?["prefetch"]?.Value<bool>() ?? false;

        string? schemaJson = null;
        var schemaToken = args["parametersSchema"] ?? nested?["parametersSchema"];
        if (schemaToken != null)
        {
            schemaJson = schemaToken.Type == JTokenType.String
                ? schemaToken.ToString()
                : schemaToken.ToString(Newtonsoft.Json.Formatting.None);
        }

        var config = new JObject
        {
            ["description"] = description,
            ["sideEffect"] = sideEffect,
            ["requiredCapability"] = requiredCapability,
            ["prefetch"] = prefetch,
            ["parametersSchema"] = schemaJson
        };

        var argMapToken = args["argumentMapping"] ?? nested?["argumentMapping"];
        if (argMapToken is JObject argMap)
        {
            config["argumentMapping"] = argMap;
        }

        return config.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string BuildProfileConfiguration(JObject args, string role)
    {
        var nested = args["configuration"] as JObject;
        var description = FirstNonEmpty(args, "description") ?? nested?["description"]?.ToString();
        var providerAlias = FirstNonEmpty(args, "providerAlias") ?? nested?["providerAlias"]?.ToString();
        var promptAlias = FirstNonEmpty(args, "promptAlias") ?? nested?["promptAlias"]?.ToString();

        double? threshold = null;
        var thresholdToken = args["autoCommitThreshold"] ?? nested?["autoCommitThreshold"];
        if (thresholdToken != null && double.TryParse(thresholdToken.ToString(), out var parsedThresh))
            threshold = parsedThresh;

        var config = new JObject
        {
            ["role"] = role,
            ["description"] = description,
            ["providerAlias"] = providerAlias,
            ["promptAlias"] = promptAlias,
            ["autoCommitThreshold"] = threshold
        };

        var toolAliasesToken = args["toolAliases"] ?? nested?["toolAliases"];
        if (toolAliasesToken is JArray toolArray)
        {
            config["toolAliases"] = toolArray;
        }

        var allowedEventsToken = args["allowedEvents"] ?? nested?["allowedEvents"];
        if (allowedEventsToken is JArray eventsArray)
        {
            config["allowedEvents"] = eventsArray;
        }

        return config.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static object ToProviderDto(PluginBindingDto binding)
    {
        var settings = binding.Configuration as AgentProviderPublicSettings
            ?? AgentProviderConfiguration.Redact(null);
        return new
        {
            alias = binding.SourceName,
            providerName = binding.ProviderName,
            binding.IsEnabled,
            model = settings.Model,
            endpoint = settings.Endpoint,
            hasApiKey = settings.HasApiKey,
            apiKeyFingerprint = settings.ApiKeyFingerprint,
            keyUpdatedAtUtc = settings.ApiKeyUpdatedAtUtc,
            isDefault = settings.IsDefault,
            maxTokens = settings.MaxTokens,
            fallbackProviderAlias = settings.FallbackProviderAlias,
            enablePromptAudit = settings.EnablePromptAudit,
            temperature = settings.Temperature,
            seed = settings.Seed,
            binding.Id,
            binding.TenantId,
            binding.CreatedAtUtc,
            binding.UpdatedAtUtc
        };
    }

    private static object ToPromptDto(PluginBindingDto binding)
    {
        var prompt = binding.Configuration as AgentPromptConfiguration
            ?? AgentPromptConfiguration.Public(null);
        return new
        {
            alias = binding.SourceName,
            kind = binding.ProviderName,
            binding.IsEnabled,
            title = prompt.Title,
            system = prompt.System,
            instructions = prompt.Body,
            binding.Id,
            binding.TenantId,
            binding.CreatedAtUtc,
            binding.UpdatedAtUtc
        };
    }

    private static string BuildProviderConfiguration(JObject args)
    {
        var nested = args["configuration"] as JObject;
        var model = FirstNonEmpty(args, "model") ?? nested?["model"]?.ToString();
        var endpoint = FirstNonEmpty(args, "endpoint") ?? nested?["endpoint"]?.ToString();
        var apiKey = FirstNonEmpty(args, "apiKey") ?? nested?["apiKey"]?.ToString();
        var isDefault = args["isDefault"]?.Value<bool>() ?? nested?["isDefault"]?.Value<bool>() ?? false;
        var maxTokens = args["maxTokens"]?.Value<int?>() ?? nested?["maxTokens"]?.Value<int?>();
        var fallbackProviderAlias = FirstNonEmpty(args, "fallbackProviderAlias") ?? nested?["fallbackProviderAlias"]?.ToString();
        var enablePromptAudit = args["enablePromptAudit"]?.Value<bool>() ?? nested?["enablePromptAudit"]?.Value<bool>() ?? false;
        var temperature = args["temperature"]?.Value<double?>() ?? nested?["temperature"]?.Value<double?>();
        var seed = args["seed"]?.Value<int?>() ?? nested?["seed"]?.Value<int?>();

        var config = new JObject
        {
            ["model"] = model,
            ["endpoint"] = endpoint,
            ["apiKey"] = apiKey,
            ["isDefault"] = isDefault,
            ["enablePromptAudit"] = enablePromptAudit
        };

        if (maxTokens.HasValue)
            config["maxTokens"] = maxTokens.Value;
        if (!string.IsNullOrWhiteSpace(fallbackProviderAlias))
            config["fallbackProviderAlias"] = fallbackProviderAlias;
        if (temperature.HasValue)
            config["temperature"] = temperature.Value;
        if (seed.HasValue)
            config["seed"] = seed.Value;

        return config.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string BuildPromptConfiguration(JObject args)
    {
        var nested = args["configuration"] as JObject;
        var title = FirstNonEmpty(args, "title") ?? nested?["title"]?.ToString();
        var system = FirstNonEmpty(args, "system") ?? nested?["system"]?.ToString();
        var instructions = FirstNonEmpty(args, "instructions", "text")
            ?? nested?["instructions"]?.ToString()
            ?? nested?["text"]?.ToString();

        return new JObject
        {
            ["title"] = title,
            ["system"] = system,
            ["instructions"] = instructions
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string? FirstNonEmpty(JObject args, params string[] names)
    {
        foreach (var name in names)
        {
            var value = args[name]?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    /// <summary>
    /// Rejects values like a bare host, a path, or an email address at write time instead of
    /// letting them surface later as a misleading provider-call failure (e.g. a 404 reported as
    /// PROVIDER_UNAVAILABLE). Only checks shape (absolute http/https URL) - not reachability.
    /// </summary>
    private static bool IsWellFormedHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static IReadOnlyDictionary<string, object?>? ParseCanonical(JToken? token)
    {
        if (token is not JObject obj || !obj.Properties().Any())
            return null;

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in obj.Properties())
            result[property.Name] = property.Value.Type == JTokenType.Null
                ? null
                : property.Value.ToObject<object>();
        return result;
    }
}
