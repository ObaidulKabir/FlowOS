using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class PluginBindingMcpTools
{
    private readonly IPluginBindingRegistryService _bindingRegistry;
    private readonly IWorkflowActionPluginRegistry? _actionPluginRegistry;
    private readonly IPolicyDecisionPluginRegistry? _decisionPluginRegistry;

    public PluginBindingMcpTools(
        IPluginBindingRegistryService bindingRegistry,
        IWorkflowActionPluginRegistry? actionPluginRegistry = null,
        IPolicyDecisionPluginRegistry? decisionPluginRegistry = null)
    {
        _bindingRegistry = bindingRegistry;
        _actionPluginRegistry = actionPluginRegistry;
        _decisionPluginRegistry = decisionPluginRegistry;
    }

    public async Task<CallToolResult> RegisterPluginBinding(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var bindingType = args["bindingType"]?.ToString()?.Trim();
            var sourceName = args["sourceName"]?.ToString()?.Trim();
            var providerName = args["providerName"]?.ToString()?.Trim();
            var isEnabled = args["isEnabled"]?.Value<bool>() ?? true;

            var normalizedType = bindingType?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalizedType == PluginBindingTypes.Prompt && string.IsNullOrWhiteSpace(providerName))
                providerName = AgentPromptKinds.Markdown;

            if (string.IsNullOrWhiteSpace(bindingType) ||
                string.IsNullOrWhiteSpace(sourceName) ||
                string.IsNullOrWhiteSpace(providerName))
            {
                return McpToolResults.Fail(
                    "MCP-ARG-001",
                    "bindingType, sourceName, and providerName are required.");
            }

            if (normalizedType != PluginBindingTypes.Action &&
                normalizedType != PluginBindingTypes.Decision &&
                normalizedType != PluginBindingTypes.Agent &&
                normalizedType != PluginBindingTypes.Prompt)
            {
                return McpToolResults.Fail(
                    "PLUGIN-BIND-001",
                    $"Unsupported bindingType '{bindingType}'. Use '{PluginBindingTypes.Action}', '{PluginBindingTypes.Decision}', '{PluginBindingTypes.Agent}', or '{PluginBindingTypes.Prompt}'.");
            }

            if (normalizedType == PluginBindingTypes.Action)
            {
                if (_actionPluginRegistry != null && !_actionPluginRegistry.TryResolve(providerName, out _))
                {
                    return McpToolResults.Fail(
                        "PLUGIN-BIND-002",
                        $"Action plugin '{providerName}' is not registered on the server.");
                }
            }
            else if (normalizedType == PluginBindingTypes.Decision)
            {
                if (_decisionPluginRegistry != null && !_decisionPluginRegistry.TryResolve(providerName, out _))
                {
                    return McpToolResults.Fail(
                        "PLUGIN-BIND-003",
                        $"Decision plugin '{providerName}' is not registered on the server.");
                }
            }
            else if (normalizedType == PluginBindingTypes.Agent)
            {
                if (!AgentProviderKinds.IsKnown(providerName))
                {
                    return McpToolResults.Fail(
                        "PLUGIN-BIND-004",
                        $"Unknown agent provider '{providerName}'. Use openai, anthropic, azure-openai, google, custom, flowos-risk, or flowos-hosted.");
                }
            }
            else if (!AgentPromptKinds.IsKnown(providerName))
            {
                return McpToolResults.Fail(
                    "PLUGIN-BIND-005",
                    $"Unknown prompt kind '{providerName}'. Use markdown or flowos-prompt.");
            }

            string? configurationJson = null;
            if ((normalizedType == PluginBindingTypes.Agent || normalizedType == PluginBindingTypes.Prompt) &&
                args["configuration"] is { Type: not JTokenType.Null } configToken)
            {
                configurationJson = configToken.Type == JTokenType.String
                    ? configToken.ToString()
                    : configToken.ToString(Newtonsoft.Json.Formatting.None);
            }

            if (normalizedType == PluginBindingTypes.Agent && AgentProviderKinds.IsFlowOsHosted(providerName))
                configurationJson = "{}";

            if (normalizedType == PluginBindingTypes.Prompt)
            {
                var parsed = AgentPromptConfiguration.Parse(configurationJson);
                if (parsed == null || string.IsNullOrWhiteSpace(parsed.Body))
                {
                    var existing = (await _bindingRegistry.ListAsync(tenantId, normalizedType, sourceName))
                        .FirstOrDefault();
                    var existingPrompt = existing?.Configuration as AgentPromptConfiguration;
                    if (existingPrompt == null || string.IsNullOrWhiteSpace(existingPrompt.Body))
                    {
                        return McpToolResults.Fail(
                            "PLUGIN-BIND-005",
                            "Prompt configuration.instructions (or text) is required when creating a prompt.");
                    }
                }
            }

            var binding = await _bindingRegistry.UpsertAsync(
                tenantId,
                normalizedType,
                sourceName,
                providerName,
                isEnabled,
                configurationJson);

            return McpToolResults.Success(ToPublicBinding(binding));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to register plugin binding: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ListPluginBindings(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var bindingType = args["bindingType"]?.ToString()?.Trim();
            var sourceName = args["sourceName"]?.ToString()?.Trim();
            bool? enabledOnly = args["enabledOnly"]?.Value<bool>();

            var bindings = await _bindingRegistry.ListAsync(tenantId, bindingType, sourceName, enabledOnly);
            return McpToolResults.Success(new
            {
                totalCount = bindings.Count,
                bindings = bindings.Select(ToPublicBinding).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list plugin bindings: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ResolvePluginBinding(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var bindingType = args["bindingType"]?.ToString()?.Trim();
            var sourceName = args["sourceName"]?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(bindingType) || string.IsNullOrWhiteSpace(sourceName))
            {
                return McpToolResults.Fail("MCP-ARG-001", "bindingType and sourceName are required.");
            }

            var normalizedType = bindingType.Trim().ToLowerInvariant();
            var resolvedProvider = await _bindingRegistry.ResolveProviderNameAsync(
                tenantId,
                normalizedType,
                sourceName);

            var isServerRegistered = false;
            if (!string.IsNullOrWhiteSpace(resolvedProvider))
            {
                isServerRegistered = normalizedType switch
                {
                    PluginBindingTypes.Action =>
                        _actionPluginRegistry?.TryResolve(resolvedProvider, out _) == true,
                    PluginBindingTypes.Decision =>
                        _decisionPluginRegistry?.TryResolve(resolvedProvider, out _) == true,
                    PluginBindingTypes.Agent =>
                        AgentProviderKinds.IsKnown(resolvedProvider),
                    PluginBindingTypes.Prompt =>
                        AgentPromptKinds.IsKnown(resolvedProvider),
                    _ => false
                };
            }

            var binding = await _bindingRegistry.GetEnabledAsync(tenantId, normalizedType, sourceName);
            return McpToolResults.Success(new
            {
                bindingType = normalizedType,
                sourceName,
                resolvedProvider,
                hasBinding = !string.IsNullOrWhiteSpace(resolvedProvider),
                isServerRegistered,
                configuration = PublicConfiguration(binding)
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to resolve plugin binding: {ex.Message}");
        }
    }

    private static object ToPublicBinding(PluginBindingDto binding) => new
    {
        binding.Id,
        binding.TenantId,
        binding.BindingType,
        binding.SourceName,
        binding.ProviderName,
        binding.IsEnabled,
        configuration = PublicConfiguration(binding),
        binding.CreatedAtUtc,
        binding.UpdatedAtUtc
    };

    private static object? PublicConfiguration(PluginBindingDto? binding)
    {
        if (binding?.Configuration is AgentProviderPublicSettings settings)
        {
            return new
            {
                model = settings.Model,
                endpoint = settings.Endpoint,
                hasApiKey = settings.HasApiKey
            };
        }

        if (binding?.Configuration is AgentPromptConfiguration prompt)
        {
            return new
            {
                title = prompt.Title,
                system = prompt.System,
                instructions = prompt.Body
            };
        }

        return binding?.Configuration;
    }
}
