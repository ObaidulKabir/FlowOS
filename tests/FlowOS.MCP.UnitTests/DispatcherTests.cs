using FlowOS.MCP.Models;
using FlowOS.MCP.Server;
using FlowOS.MCP.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public sealed class DispatcherTests
{
    private readonly TestRegistry _registry = new();

    [Fact]
    public async Task Initialize_ping_list_and_call_are_supported()
    {
        var dispatcher = new McpJsonRpcDispatcher(_registry);

        var initialize = await dispatcher.DispatchAsync(Request(1, "initialize", new
        {
            protocolVersion = McpJsonRpcDispatcher.SupportedProtocolVersion,
            clientInfo = new { name = "tests", version = "1" },
            capabilities = new { }
        }));
        var initializeResponse = Assert.IsType<JsonRpcResponse>(initialize.Response);
        Assert.Equal(McpJsonRpcDispatcher.SupportedProtocolVersion,
            JObject.FromObject(initializeResponse.Result!)["protocolVersion"]?.ToString());

        var ping = Assert.IsType<JsonRpcResponse>((await dispatcher.DispatchAsync(Request(2, "ping"))).Response);
        Assert.Null(ping.Error);

        var list = Assert.IsType<JsonRpcResponse>((await dispatcher.DispatchAsync(Request(3, "tools/list"))).Response);
        Assert.Equal(10, JObject.FromObject(list.Result!)["tools"]!.Count());

        var call = Assert.IsType<JsonRpcResponse>((await dispatcher.DispatchAsync(
            Request(4, "tools/call", new { name = "tool-1", arguments = new { value = 42 } }))).Response);
        var result = Assert.IsType<CallToolResult>(call.Result);
        Assert.False(result.IsError);
    }

    [Theory]
    [InlineData("{", -32700)]
    [InlineData("{}", -32600)]
    [InlineData("""{"jsonrpc":"1.0","id":1,"method":"ping"}""", -32600)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"missing"}""", -32601)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"missing"}}""", -32602)]
    public async Task Standard_errors_are_returned(string json, int expectedCode)
    {
        var response = Assert.IsType<JsonRpcResponse>(
            (await new McpJsonRpcDispatcher(_registry).DispatchAsync(json)).Response);
        Assert.Equal(expectedCode, response.Error?.Code);
    }

    [Fact]
    public async Task Batch_omits_notifications_and_notification_only_has_no_response()
    {
        var dispatcher = new McpJsonRpcDispatcher(_registry);
        var notification = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

        var onlyNotification = await dispatcher.DispatchAsync($"[{notification}]");
        Assert.Equal(McpDispatchKind.NoResponse, onlyNotification.Kind);
    }

    [Fact]
    public async Task Prompts_and_resources_are_supported()
    {
        var dispatcher = new McpJsonRpcDispatcher(_registry);

        // 1. Initialize returns instructions and capabilities
        var init = await dispatcher.DispatchAsync(Request(1, "initialize", new
        {
            protocolVersion = McpJsonRpcDispatcher.SupportedProtocolVersion,
            clientInfo = new { name = "tests", version = "1" },
            capabilities = new { }
        }));
        var initResult = JObject.FromObject(((JsonRpcResponse)init.Response!).Result!);
        Assert.NotNull(initResult["instructions"]);
        Assert.NotNull(initResult["capabilities"]?["prompts"]);
        Assert.NotNull(initResult["capabilities"]?["resources"]);

        // 2. Prompts list & get
        var promptsList = await dispatcher.DispatchAsync(Request(2, "prompts/list"));
        var promptsResult = JObject.FromObject(((JsonRpcResponse)promptsList.Response!).Result!);
        Assert.NotEmpty(promptsResult["prompts"]!);

        var promptGet = await dispatcher.DispatchAsync(Request(3, "prompts/get", new { name = "operate_workflow_process" }));
        var promptGetResult = JObject.FromObject(((JsonRpcResponse)promptGet.Response!).Result!);
        Assert.NotNull(promptGetResult["messages"]);

        // 3. Resources list & read
        var resourcesList = await dispatcher.DispatchAsync(Request(4, "resources/list"));
        var resourcesResult = JObject.FromObject(((JsonRpcResponse)resourcesList.Response!).Result!);
        Assert.NotEmpty(resourcesResult["resources"]!);

        var resourceRead = await dispatcher.DispatchAsync(Request(5, "resources/read", new { uri = "flowos://guides/lifecycle" }));
        var resourceReadResult = JObject.FromObject(((JsonRpcResponse)resourceRead.Response!).Result!);
        Assert.NotNull(resourceReadResult["contents"]);
    }

    [Fact]
    public async Task Batch_omits_notifications_and_notification_only_has_no_response_batch_test()
    {
        var dispatcher = new McpJsonRpcDispatcher(_registry);
        var notification = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

        var batch = await dispatcher.DispatchAsync(
            $"[{notification},{Request(7, "ping")}]");
        var responses = Assert.IsAssignableFrom<IEnumerable<JsonRpcResponse>>(batch.Response);
        Assert.Single(responses);
    }

    private static string Request(object id, string method, object? parameters = null) =>
        JsonConvert.SerializeObject(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

    private sealed class TestRegistry : IToolRegistry
    {
        private readonly IReadOnlyList<McpTool> _tools = Enumerable.Range(1, 10)
            .Select(index => new McpTool
            {
                Name = $"tool-{index}",
                Description = "Test tool",
                InputSchema = McpToolSchemas.NoArguments()
            })
            .ToList();

        public void Register(string name, string description, object schema, Func<JObject, Task<CallToolResult>> handler) =>
            throw new NotSupportedException();

        public IEnumerable<McpTool> GetTools() => _tools;
        public bool Contains(string name) => _tools.Any(tool => tool.Name == name);

        public Task<CallToolResult> ExecuteAsync(string name, JObject arguments) =>
            Task.FromResult(McpToolResults.Success(new { arguments }));
    }
}
