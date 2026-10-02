using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations.Adapters;
using Xunit;

namespace FlowOS.UnitTests.Agents;

public class OpenAiProviderAdapterTests
{
    [Fact]
    public void ParseResponse_Extracts_Content_And_Tokens()
    {
        var adapter = new OpenAiProviderAdapter();
        var response = "{\"id\":\"chatcmpl-xyz\",\"choices\":[{\"message\":{\"content\":\"{\\\"insight\\\":\\\"hi\\\",\\\"eventType\\\":\\\"X\\\"}\"}}],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":50,\"total_tokens\":150}}";

        var parsed = adapter.ParseResponse(response);

        Assert.Equal("{\"insight\":\"hi\",\"eventType\":\"X\"}", parsed.Content);
        Assert.Equal(100, parsed.InputTokens);
        Assert.Equal(50, parsed.OutputTokens);
        Assert.Equal(150, parsed.TotalTokens);
        Assert.Equal("chatcmpl-xyz", parsed.ProviderRequestId);
    }

    [Fact]
    public void ParseResponse_Extracts_ToolCalls()
    {
        var adapter = new OpenAiProviderAdapter();
        var response = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"type\":\"function\",\"id\":\"call-1\",\"function\":{\"name\":\"lookup\",\"arguments\":\"{\\\"k\\\":1}\"}}]}}]}";

        var parsed = adapter.ParseResponse(response);

        Assert.NotNull(parsed.ToolCalls);
        Assert.Single(parsed.ToolCalls);
        Assert.Equal("call-1", parsed.ToolCalls[0].Id);
        Assert.Equal("lookup", parsed.ToolCalls[0].ToolName);
        Assert.Contains("\"k\":1", parsed.ToolCalls[0].ArgumentsJson);
    }

    [Fact]
    public async Task CreateRequest_Uses_Bearer_And_Correct_Body()
    {
        var adapter = new OpenAiProviderAdapter();
        var request = adapter.CreateRequest(
            endpoint: "",
            apiKey: "sk-test",
            model: "gpt-4o-mini",
            systemPrompt: "be good",
            userPrompt: "do it");

        Assert.Equal("https://api.openai.com/v1/chat/completions", request.RequestUri?.ToString());
        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
        Assert.Equal("sk-test", request.Headers.Authorization.Parameter);

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("gpt-4o-mini", root.GetProperty("model").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("be good", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("do it", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public void ParseResponse_InvalidJson_Returns_NullContent()
    {
        var adapter = new OpenAiProviderAdapter();
        var parsed = adapter.ParseResponse("not json");

        Assert.Null(parsed.Content);
    }
}

public class AnthropicProviderAdapterTests
{
    [Fact]
    public void ParseResponse_Extracts_Text_From_Content_Blocks_And_Tokens()
    {
        var adapter = new AnthropicProviderAdapter();
        var response = "{\"id\":\"msg_1\",\"content\":[{\"type\":\"text\",\"text\":\"Hello\"}],\"usage\":{\"input_tokens\":10,\"output_tokens\":20}}";

        var parsed = adapter.ParseResponse(response);

        Assert.Equal("Hello", parsed.Content);
        Assert.Equal(10, parsed.InputTokens);
        Assert.Equal(20, parsed.OutputTokens);
        Assert.Equal(30, parsed.TotalTokens);
    }

    [Fact]
    public void ParseResponse_Extracts_ToolUse_Blocks()
    {
        var adapter = new AnthropicProviderAdapter();
        var response = "{\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"search\",\"input\":{\"q\":\"abc\"}}]}";

        var parsed = adapter.ParseResponse(response);

        Assert.NotNull(parsed.ToolCalls);
        Assert.Single(parsed.ToolCalls);
        Assert.Equal("toolu_1", parsed.ToolCalls[0].Id);
        Assert.Equal("search", parsed.ToolCalls[0].ToolName);
        Assert.Contains("q", parsed.ToolCalls[0].ArgumentsJson);
    }

    [Fact]
    public void ParseResponse_Overloaded_Error_Classified_As_Unavailable()
    {
        var adapter = new AnthropicProviderAdapter();
        var response = "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"busy\"}}";

        var parsed = adapter.ParseResponse(response);

        Assert.Equal(AgentFailureCodes.ProviderUnavailable, parsed.ErrorCode);
    }

    [Fact]
    public void ParseResponse_RateLimit_Error_Classified()
    {
        var adapter = new AnthropicProviderAdapter();
        var response = "{\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\"}}";

        var parsed = adapter.ParseResponse(response);

        Assert.Equal(AgentFailureCodes.ProviderRateLimit, parsed.ErrorCode);
    }

    [Fact]
    public async Task CreateRequest_Uses_XApiKey_Header()
    {
        var adapter = new AnthropicProviderAdapter();
        var request = adapter.CreateRequest(
            endpoint: "",
            apiKey: "sk-ant-123",
            model: "claude-3-sonnet",
            systemPrompt: "sys",
            userPrompt: "user");

        Assert.True(request.Headers.TryGetValues("x-api-key", out var apiKeys));
        Assert.Equal("sk-ant-123", Assert.Single(apiKeys));
        Assert.True(request.Headers.Contains("anthropic-version"));

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("claude-3-sonnet", root.GetProperty("model").GetString());
        Assert.True(root.TryGetProperty("max_tokens", out _));
        Assert.Equal("sys", root.GetProperty("system").GetString());

        var messages = root.GetProperty("messages");
        Assert.Single(messages.EnumerateArray());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[0].GetProperty("content").GetString());
    }
}

public class GoogleProviderAdapterTests
{
    [Fact]
    public void ParseResponse_Extracts_Text_From_Candidate_Parts()
    {
        var adapter = new GoogleProviderAdapter();
        var response = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"greetings\"}]}}]}";

        var parsed = adapter.ParseResponse(response);

        Assert.Equal("greetings", parsed.Content);
    }

    [Fact]
    public void ParseResponse_Extracts_FunctionCalls()
    {
        var adapter = new GoogleProviderAdapter();
        var response = "{\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{\"name\":\"invoke\",\"args\":{\"x\":1}}}]}}],\"usageMetadata\":{\"promptTokenCount\":5,\"candidatesTokenCount\":8,\"totalTokenCount\":13}}";

        var parsed = adapter.ParseResponse(response);

        Assert.NotNull(parsed.ToolCalls);
        Assert.Single(parsed.ToolCalls);
        Assert.Equal("invoke", parsed.ToolCalls[0].ToolName);
        Assert.Equal(5, parsed.InputTokens);
        Assert.Equal(8, parsed.OutputTokens);
        Assert.Equal(13, parsed.TotalTokens);
    }

    [Fact]
    public void ParseResponse_SAFETY_FinishReason_Returns_InvalidModelOutput_Error()
    {
        var adapter = new GoogleProviderAdapter();
        var response = "{\"candidates\":[{\"finishReason\":\"SAFETY\",\"content\":{}}]}";

        var parsed = adapter.ParseResponse(response);

        Assert.Equal(AgentFailureCodes.InvalidModelOutput, parsed.ErrorCode);
    }

    [Fact]
    public void ParseResponse_RECITATION_Returns_InvalidModelOutput_Error()
    {
        var adapter = new GoogleProviderAdapter();
        var response = "{\"candidates\":[{\"finishReason\":\"RECITATION\"}]}";

        var parsed = adapter.ParseResponse(response);

        Assert.Equal(AgentFailureCodes.InvalidModelOutput, parsed.ErrorCode);
    }

    [Fact]
    public async Task CreateRequest_Uses_XGoogApiKey_Header_And_Model_Endpoint()
    {
        var adapter = new GoogleProviderAdapter();
        var request = adapter.CreateRequest(
            endpoint: "",
            apiKey: "goog-key",
            model: "gemini-1.5-flash",
            systemPrompt: "sys",
            userPrompt: "hi");

        var uri = request.RequestUri?.ToString() ?? "";
        Assert.Contains("gemini-1.5-flash:generateContent", uri);
        Assert.Contains("generativelanguage.googleapis.com", uri);

        Assert.True(request.Headers.TryGetValues("x-goog-api-key", out var apiKeys));
        Assert.Equal("goog-key", Assert.Single(apiKeys));

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("sys", root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("hi", root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
    }
}
