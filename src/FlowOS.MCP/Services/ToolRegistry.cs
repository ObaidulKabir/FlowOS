using FlowOS.MCP.Models;
using FlowOS.Application.Common.Interfaces;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowOS.MCP.Services
{
    public class ToolRegistry : IToolRegistry
    {
        private readonly Dictionary<string, (McpTool Tool, Func<JObject, Task<CallToolResult>> Handler)> _tools = new();
        private readonly ILogger<ToolRegistry> _logger;
        private readonly IServiceScopeFactory? _scopeFactory;

        public ToolRegistry(ILogger<ToolRegistry> logger, IServiceScopeFactory? scopeFactory = null)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public void Register(string name, string description, object schema, Func<JObject, Task<CallToolResult>> handler)
        {
            _tools[name] = (new McpTool
            {
                Name = name,
                Description = description,
                InputSchema = schema ?? new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                }
            }, handler);
        }

        public IEnumerable<McpTool> GetTools()
        {
            return _tools.Values.Select(x => x.Tool);
        }

        public bool Contains(string name) => _tools.ContainsKey(name);

        public async Task<CallToolResult> ExecuteAsync(string name, JObject arguments)
        {
            if (!_tools.TryGetValue(name, out var entry))
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Tool '{name}' was not found.");
            }

            try
            {
                using var scope = _scopeFactory?.CreateScope();
                var entitlement = scope?.ServiceProvider.GetService<ITenantEntitlementService>();
                if (entitlement != null)
                {
                    var profile = McpToolDescriptions.ProfileFor(name);
                    if (entitlement.McpToolRequiresPaidPlan(name, profile.Mutating, profile.SideEffect))
                    {
                        var decision = await entitlement.EnsureRuntimeAllowedAsync(McpRequestContext.TenantId);
                        if (!decision.Allowed)
                        {
                            return McpToolResults.Fail(
                                decision.Code ?? TenantEntitlementPolicy.PlanRequiredCode,
                                decision.Message ?? TenantEntitlementPolicy.PlanRequiredMessage);
                        }
                    }
                }

                return await entry.Handler(arguments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception executing MCP tool {ToolName}", name);
                return McpToolResults.Fail("MCP-INTERNAL", "The tool failed unexpectedly.");
            }
        }
    }
}
