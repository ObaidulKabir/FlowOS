using FlowOS.MCP.Models;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace FlowOS.MCP.Services
{
    public interface IToolRegistry
    {
        void Register(string name, string description, object schema, Func<JObject, Task<CallToolResult>> handler);
        IEnumerable<McpTool> GetTools();
        Task<CallToolResult> ExecuteAsync(string name, JObject arguments);
    }
}
