using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
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

            if (string.IsNullOrWhiteSpace(bindingType) ||
                string.IsNullOrWhiteSpace(sourceName) ||
                string.IsNullOrWhiteSpace(providerName))
            {
                return McpToolResults.Fail(
                    "MCP-ARG-001",
                    "bindingType, sourceName, and providerName are required.");
            }

            var normalizedType = bindingType.Trim().ToLowerInvariant();
            if (normalizedType != PluginBindingTypes.Action && normalizedType != PluginBindingTypes.Decision)
            {
                return McpToolResults.Fail(
                    "PLUGIN-BIND-001",
                    $"Unsupported bindingType '{bindingType}'. Use '{PluginBindingTypes.Action}' or '{PluginBindingTypes.Decision}'.");
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
            else
            {
                if (_decisionPluginRegistry != null && !_decisionPluginRegistry.TryResolve(providerName, out _))
                {
                    return McpToolResults.Fail(
                        "PLUGIN-BIND-003",
                        $"Decision plugin '{providerName}' is not registered on the server.");
                }
            }

            var binding = await _bindingRegistry.UpsertAsync(
                tenantId,
                normalizedType,
                sourceName,
                providerName,
                isEnabled);

            return McpToolResults.Success(new
            {
                binding.Id,
                binding.TenantId,
                binding.BindingType,
                binding.SourceName,
                binding.ProviderName,
                binding.IsEnabled,
                binding.CreatedAtUtc,
                binding.UpdatedAtUtc
            });
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
                bindings = bindings.Select(b => new
                {
                    b.Id,
                    b.TenantId,
                    b.BindingType,
                    b.SourceName,
                    b.ProviderName,
                    b.IsEnabled,
                    b.CreatedAtUtc,
                    b.UpdatedAtUtc
                }).ToList()
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
                isServerRegistered = normalizedType == PluginBindingTypes.Action
                    ? _actionPluginRegistry?.TryResolve(resolvedProvider, out _) == true
                    : _decisionPluginRegistry?.TryResolve(resolvedProvider, out _) == true;
            }

            return McpToolResults.Success(new
            {
                bindingType = normalizedType,
                sourceName,
                resolvedProvider,
                hasBinding = !string.IsNullOrWhiteSpace(resolvedProvider),
                isServerRegistered
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
}
