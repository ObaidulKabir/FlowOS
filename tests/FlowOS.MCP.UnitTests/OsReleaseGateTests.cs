using FlowOS.MCP.Models;
using FlowOS.MCP.Server;
using FlowOS.MCP.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public sealed class OsReleaseGateTests
{
    private static readonly string[] MustPassIds =
    {
        "OS-ID", "OS-KERNEL", "OS-LAW", "OS-INBOX", "OS-INT",
        "OS-AI", "OS-SIM", "OS-OPS", "OS-COMM"
    };

    [Fact]
    public void Guide_Exposes_Parseable_Contract_And_Forbids_Os_Claim_While_Red()
    {
        var guide = FlowOsMcpGuidance.OsReleaseGateGuide;
        var fields = ParseContract(guide);

        Assert.Equal("OS-1 Honesty Gate", fields["GATE"]);
        Assert.Contains(fields["VERDICT"], new[] { "RED", "GREEN" });
        Assert.Contains(fields["CLAIM_ALLOWED"], new[] { "true", "false" });

        foreach (var id in MustPassIds)
        {
            Assert.True(fields.ContainsKey(id), $"Missing must-pass status for {id}");
            Assert.Contains(fields[id], new[] { "done", "partial", "missing" });
        }

        if (fields["VERDICT"] == "GREEN")
        {
            Assert.Equal("true", fields["CLAIM_ALLOWED"]);
            foreach (var id in MustPassIds)
            {
                Assert.Equal("done", fields[id]);
            }
        }
        else
        {
            Assert.Equal("false", fields["CLAIM_ALLOWED"]);
            Assert.Contains("not yet a business automation operating system", guide);
        }

        Assert.Contains("DecisionPacket", guide);
        Assert.Contains("autoCommit", guide);
        Assert.Contains("HumanTask", guide);
        Assert.Contains("MCP-PLAN-REQUIRED", guide);
        Assert.Contains("dual-kernel", guide);
    }

    [Fact]
    public void Docs_Chapter_Machine_Contract_Matches_Mcp_Guide()
    {
        var docsPath = FindRepoFile(Path.Combine("docs", "19-os-release-gate.md"));
        Assert.False(string.IsNullOrWhiteSpace(docsPath), "docs/19-os-release-gate.md was not found.");
        var docs = ParseContract(File.ReadAllText(docsPath!));
        var mcp = ParseContract(FlowOsMcpGuidance.OsReleaseGateGuide);

        Assert.Equal(mcp["GATE"], docs["GATE"]);
        Assert.Equal(mcp["VERDICT"], docs["VERDICT"]);
        Assert.Equal(mcp["CLAIM_ALLOWED"], docs["CLAIM_ALLOWED"]);
        foreach (var id in MustPassIds)
        {
            Assert.Equal(mcp[id], docs[id]);
        }
    }

    [Fact]
    public async Task Prompt_And_Resource_Serve_The_Same_Gate()
    {
        var dispatcher = new McpJsonRpcDispatcher(new EmptyRegistry());

        var prompt = await dispatcher.DispatchAsync(Request(1, "prompts/get", new { name = "check_os_release_gate" }));
        var promptText = JObject.FromObject(((JsonRpcResponse)prompt.Response!).Result!)
            ["messages"]![0]!["content"]!["text"]!.ToString();

        var resource = await dispatcher.DispatchAsync(Request(2, "resources/read", new { uri = "flowos://guides/os-release-gate" }));
        var resourceText = JObject.FromObject(((JsonRpcResponse)resource.Response!).Result!)
            ["contents"]![0]!["text"]!.ToString();

        Assert.Equal(FlowOsMcpGuidance.OsReleaseGateGuide, promptText);
        Assert.Equal(FlowOsMcpGuidance.OsReleaseGateGuide, resourceText);
        Assert.Contains("VERDICT=GREEN", resourceText);
        Assert.Contains("CLAIM_ALLOWED=true", resourceText);
    }

    private static Dictionary<string, string> ParseContract(string guide)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in guide.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq];
            if (key is "GATE" or "VERDICT" or "CLAIM_ALLOWED" || key.StartsWith("OS-", StringComparison.Ordinal))
            {
                fields[key] = line[(eq + 1)..].Trim();
            }
        }

        return fields;
    }

    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static string Request(object id, string method, object? parameters = null) =>
        JsonConvert.SerializeObject(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

    private sealed class EmptyRegistry : IToolRegistry
    {
        public void Register(string name, string description, object schema, Func<JObject, Task<CallToolResult>> handler) =>
            throw new NotSupportedException();

        public IEnumerable<McpTool> GetTools() => Array.Empty<McpTool>();
        public bool Contains(string name) => false;

        public Task<CallToolResult> ExecuteAsync(string name, JObject arguments) =>
            Task.FromResult(new CallToolResult { IsError = true });
    }
}
