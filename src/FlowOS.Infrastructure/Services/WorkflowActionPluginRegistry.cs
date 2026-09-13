using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FlowOS.Infrastructure.Services;

public sealed class WorkflowActionPluginRegistry : IWorkflowActionPluginRegistry
{
    private readonly Dictionary<string, IWorkflowActionPlugin> _plugins;
    private readonly bool _allowWildcardFallback;

    public WorkflowActionPluginRegistry(
        IEnumerable<IWorkflowActionPlugin> plugins,
        IConfiguration? configuration = null)
    {
        _plugins = plugins.ToDictionary(
            p => p.ActionType,
            p => p,
            StringComparer.OrdinalIgnoreCase);

        _allowWildcardFallback = !bool.TryParse(
            configuration?["FlowOS:Actions:AllowWildcardPluginFallback"],
            out var configuredFallback) || configuredFallback;
    }

    public bool TryResolve(string actionType, out IWorkflowActionPlugin plugin)
    {
        plugin = null!;
        if (string.IsNullOrWhiteSpace(actionType)) return false;

        if (_plugins.TryGetValue(actionType, out var exactPlugin) && exactPlugin != null)
        {
            plugin = exactPlugin;
            return true;
        }

        if (_allowWildcardFallback && _plugins.TryGetValue("*", out var wildcardPlugin) && wildcardPlugin != null)
        {
            plugin = wildcardPlugin;
            return true;
        }

        return false;
    }
}
