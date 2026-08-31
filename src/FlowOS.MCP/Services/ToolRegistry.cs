using FlowOS.MCP.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FlowOS.MCP.Services
{
    public class ToolRegistry : IToolRegistry
    {
        private readonly Dictionary<string, (McpTool Tool, Func<JObject, Task<CallToolResult>> Handler)> _tools = new();

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

        public async Task<CallToolResult> ExecuteAsync(string name, JObject arguments)
        {
            if (!_tools.TryGetValue(name, out var entry))
            {
                return new CallToolResult 
                { 
                    IsError = true, 
                    Content = new List<ToolContent> { new ToolContent { Text = $"Tool '{name}' not found." } } 
                };
            }

            try
            {
                return await entry.Handler(arguments);
            }
            catch (Exception ex)
            {
                return new CallToolResult 
                { 
                    IsError = true, 
                    Content = new List<ToolContent> { new ToolContent { Text = $"Error executing tool '{name}': {ex.Message}\n{ex.StackTrace}" } } 
                };
            }
        }
    }
}
