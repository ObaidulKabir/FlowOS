using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.MCP.Server
{
    public class McpServer
    {
        private readonly IToolRegistry _toolRegistry;
        private readonly TextReader _input;
        private readonly TextWriter _output;

        public McpServer(IToolRegistry toolRegistry)
        {
            _toolRegistry = toolRegistry;
            _input = Console.In;
            _output = Console.Out;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await _input.ReadLineAsync();
                if (line == null) break;

                try
                {
                    await HandleRequestAsync(line);
                }
                catch (Exception ex)
                {
                    // Log fatal error to stderr, don't crash
                    Console.Error.WriteLine($"Fatal Error: {ex.Message}");
                }
            }
        }

        private async Task HandleRequestAsync(string json)
        {
            JsonRpcRequest? request = null;
            try
            {
                request = JsonConvert.DeserializeObject<JsonRpcRequest>(json);
            }
            catch
            {
                // Invalid JSON
                return;
            }

            if (request == null) return;

            object? result = null;
            JsonRpcError? error = null;

            try
            {
                switch (request.Method)
                {
                    case "initialize":
                        result = new
                        {
                            protocolVersion = "2024-11-05",
                            capabilities = new
                            {
                                tools = new { listChanged = false }
                            },
                            serverInfo = new
                            {
                                name = "FlowOS MCP Server",
                                version = "1.0.0"
                            }
                        };
                        break;
                    
                    case "notifications/initialized":
                         // No response needed
                         return;

                    case "tools/list":
                        result = new { tools = _toolRegistry.GetTools() };
                        break;

                    case "tools/call":
                        if (request.Params == null) throw new ArgumentException("Params required");
                        var callParams = request.Params.ToObject<CallToolParams>();
                        if (callParams == null) throw new ArgumentException("Invalid params");
                        
                        var toolResult = await _toolRegistry.ExecuteAsync(callParams.Name, callParams.Arguments);
                        result = toolResult; // MCP expects the result directly
                        break;

                    default:
                        // Ignore unknown notifications, error on requests
                        if (request.Id != null)
                        {
                            error = new JsonRpcError { Code = -32601, Message = "Method not found" };
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                error = new JsonRpcError { Code = -32000, Message = ex.Message };
            }

            // Send Response if ID is present
            if (request.Id != null)
            {
                var response = new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = result,
                    Error = error
                };

                string responseJson = JsonConvert.SerializeObject(response, Formatting.None);
                await _output.WriteLineAsync(responseJson);
                await _output.FlushAsync();
            }
        }
    }
}
