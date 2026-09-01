using FlowOS.MCP.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FlowOS.MCP.Services
{
    public class ToolRegistry : IToolRegistry
    {
        private readonly Dictionary<string, (McpTool Tool, Func<JObject, Task<CallToolResult>> Handler)> _tools = new();
        private readonly ILogger<ToolRegistry> _logger;

        public ToolRegistry(ILogger<ToolRegistry> logger)
        {
            _logger = logger;
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
