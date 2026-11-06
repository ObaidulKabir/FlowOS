using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
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

public class PluginBindingMcpTools
{
    private readonly IPluginBindingRegistryService _bindingRegistry;
    private readonly IWorkflowActionPluginRegistry? _actionPluginRegistry;
    private readonly IPolicyDecisionPluginRegistry? _decisionPluginRegistry;
    private readonly IConfiguration? _configuration;
    private readonly ICurrentUser? _currentUser;
    private readonly ICapabilityService? _capabilityService;

    public PluginBindingMcpTools(
        IPluginBindingRegistryService bindingRegistry,
        IWorkflowActionPluginRegistry? actionPluginRegistry = null,
        IPolicyDecisionPluginRegistry? decisionPluginRegistry = null,
        IConfiguration? configuration = null,
        ICurrentUser? currentUser = null,
        ICapabilityService? capabilityService = null)
    {
        _bindingRegistry = bindingRegistry;
        _actionPluginRegistry = actionPluginRegistry;
        _decisionPluginRegistry = decisionPluginRegistry;
        _configuration = configuration;
        _currentUser = currentUser;
        _capabilityService = capabilityService;
    }

    /// <summary>
    /// Same gate as <c>AgentContextMcpTools.RequireAgentManageCapabilityAsync</c> - only applies to the
    /// bindingType "agent" path of <see cref="RegisterPluginBinding"/>. Null currentUser/capabilityService
    /// only happens in unit tests constructed directly; production DI always supplies both.
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

    private static bool IsWellFormedHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

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
                var deniedAgentManage = await RequireAgentManageCapabilityAsync(tenantId, "register_plugin_binding");
                if (deniedAgentManage != null)
                    return deniedAgentManage;

                if (!AgentProviderKinds.IsKnown(providerName))
                {
                    return McpToolResults.Fail(
                        "PLUGIN-BIND-004",
                        $"Unknown agent provider '{providerName}'. Use openai, anthropic, azure-openai, google, custom, flowos-risk, or flowos-hosted.");
                }

                if (AgentProviderKinds.IsReservedAlias(sourceName) && !AgentProviderKinds.IsFlowOsHosted(providerName))
                {
                    return McpToolResults.Fail(
                        "AGENT-ALIAS-RESERVED",
                        "The alias 'flowos-hosted' is platform-reserved: a step whose agentProvider resolves to it always gets the platform's hosted lease at runtime, regardless of what is saved here. Rejected to prevent silently orphaned configuration - use a different alias for a BYO providerName key.");
                }

                if (!AgentProviderKinds.IsFlowOsHosted(providerName))
                {
                    var endpointCandidate = (args["configuration"] as JObject)?["endpoint"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(endpointCandidate))
                    {
                        if (!IsWellFormedHttpUrl(endpointCandidate))
                        {
                            return McpToolResults.Fail(
                                "MCP-ARG-001",
                                "configuration.endpoint must be a full http(s) URL, not a bare host, path, or email address.");
                        }

                        var endpointScheme = new Uri(endpointCandidate, UriKind.Absolute).Scheme;
                        if (!IsEndpointSchemeAllowed(endpointScheme))
                        {
                            return McpToolResults.Fail(
                                "MCP-ARG-001",
                                "configuration.endpoint must be https:// outside Development - a BYO API key is sent to it as a Bearer token.");
                        }
                    }
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
                hasApiKey = settings.HasApiKey,
                apiKeyFingerprint = settings.ApiKeyFingerprint,
                keyUpdatedAtUtc = settings.ApiKeyUpdatedAtUtc
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
