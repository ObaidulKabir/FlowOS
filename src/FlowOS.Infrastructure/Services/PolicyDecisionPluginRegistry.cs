using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Core.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

public sealed class PolicyDecisionPluginRegistry : IPolicyDecisionPluginRegistry
{
    private readonly Dictionary<string, IPolicyDecisionPlugin> _plugins;

    public PolicyDecisionPluginRegistry(IEnumerable<IPolicyDecisionPlugin> plugins)
    {
        _plugins = plugins.ToDictionary(
            p => p.ProviderName,
            p => p,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool TryResolve(string providerName, out IPolicyDecisionPlugin plugin)
    {
        plugin = null!;
        if (string.IsNullOrWhiteSpace(providerName)) return false;

        if (_plugins.TryGetValue(providerName, out var resolved) && resolved != null)
        {
            plugin = resolved;
            return true;
        }

        return false;
    }
}
