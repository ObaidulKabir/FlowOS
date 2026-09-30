using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class AgentContextMcpTools
{
    private readonly IDecisionPacketBuilder _packetBuilder;
    private readonly IPluginBindingRegistryService _pluginBindings;
    private readonly IAgentToolHost? _toolHost;

    public AgentContextMcpTools(
        IDecisionPacketBuilder packetBuilder,
        IPluginBindingRegistryService pluginBindings,
        IAgentToolHost? toolHost = null)
    {
        _packetBuilder = packetBuilder;
        _pluginBindings = pluginBindings;
        _toolHost = toolHost;
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

            var prefetch = args["prefetch"]?.Value<bool>() ?? true;
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

        return new JObject
        {
            ["model"] = model,
            ["endpoint"] = endpoint,
            ["apiKey"] = apiKey
        }.ToString(Newtonsoft.Json.Formatting.None);
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
