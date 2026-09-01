using FlowOS.MCP.Services;
using Newtonsoft.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.MCP.Server
{
    /// <summary>
    /// Stdio MCP transport: newline-delimited JSON-RPC on Console.In / Console.Out.
    /// </summary>
    public class McpServer
    {
        private readonly IMcpJsonRpcDispatcher _dispatcher;
        private readonly TextReader _input;
        private readonly TextWriter _output;

        public McpServer(IMcpJsonRpcDispatcher dispatcher)
            : this(dispatcher, Console.In, Console.Out)
        {
        }

        public McpServer(
            IMcpJsonRpcDispatcher dispatcher,
            TextReader input,
            TextWriter output)
        {
            _dispatcher = dispatcher;
            _input = input;
            _output = output;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await _input.ReadLineAsync(cancellationToken);
                if (line == null) break;

                try
                {
                    var outcome = await _dispatcher.DispatchAsync(line, cancellationToken);
                    if (outcome.Kind == McpDispatchKind.Response && outcome.Response != null)
                    {
                        string responseJson = JsonConvert.SerializeObject(outcome.Response, Formatting.None);
                        await _output.WriteLineAsync(responseJson);
                        await _output.FlushAsync();
                    }
                }
                catch (Exception ex)
                {
                    // Log fatal error to stderr, don't crash
                    Console.Error.WriteLine($"Fatal Error: {ex.Message}");
                }
            }
        }
    }
}
