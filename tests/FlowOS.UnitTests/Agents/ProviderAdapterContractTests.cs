using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations.Adapters;
using FlowOS.Core.Common.Models;
using Xunit;

namespace FlowOS.UnitTests.Agents;

/// <summary>
/// Tenant integration finding N5: AgentProviderConfiguration.Temperature/Seed must actually reach
/// the constructed adapter instance through each factory, not just exist as unused config fields.
/// </summary>
public class LlmProviderAdapterRegistrySamplingParamsTests
{
    private static readonly LlmProviderAdapterRegistry Registry = new(new ILlmProviderAdapterFactory[]
    {
        new OpenAiAdapterFactory(),
        new AzureOpenAiAdapterFactory(),
        new CustomOpenAiAdapterFactory(),
        new AnthropicAdapterFactory(),
        new GoogleAdapterFactory()
    });

    [Theory]
    [InlineData("openai")]
    [InlineData("azure-openai")]
    [InlineData("custom")]
    public void OpenAiCompatibleFactories_PassThroughTemperatureAndSeed(string providerName)
    {
        var adapter = Registry.GetAdapter(providerName, new AgentProviderConfiguration { Temperature = 0.6, Seed = 5 });
        Assert.Equal(((double?)0.6, (int?)5), adapter.EffectiveSamplingParams);
    }

    [Fact]
    public void AnthropicFactory_PassesThroughTemperature_ButNeverASeed()
    {
        var adapter = Registry.GetAdapter("anthropic", new AgentProviderConfiguration { Temperature = 0.1, Seed = 99, MaxTokens = 2048 });
        Assert.Equal(((double?)0.1, (int?)null), adapter.EffectiveSamplingParams);
    }

    [Fact]
    public void GoogleFactory_PassesThroughTemperature_ButNeverASeed()
    {
        var adapter = Registry.GetAdapter("google", new AgentProviderConfiguration { Temperature = 0.9, Seed = 99 });
        Assert.Equal(((double?)0.9, (int?)null), adapter.EffectiveSamplingParams);
    }

    [Fact]
    public void NullConfig_LeavesSamplingParamsUnset()
    {
        var adapter = Registry.GetAdapter("openai", config: null);
        Assert.Equal(((double?)null, (int?)null), adapter.EffectiveSamplingParams);
    }
}

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

    /// <summary>Tenant integration finding N5: temperature/seed, when configured, are sent as top-level
    /// request fields and reported back via EffectiveSamplingParams for audit visibility.</summary>
    [Fact]
    public async Task CreateRequest_WithTemperatureAndSeed_IncludesBothInPayload()
    {
        var adapter = new OpenAiProviderAdapter(temperature: 0.4, seed: 42);
        Assert.Equal(((double?)0.4, (int?)42), adapter.EffectiveSamplingParams);

        var request = adapter.CreateRequest(
            endpoint: "",
            apiKey: "sk-test",
            model: "gpt-4o-mini",
            systemPrompt: "be good",
            userPrompt: "do it");

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal(0.4, root.GetProperty("temperature").GetDouble());
        Assert.Equal(42, root.GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task CreateRequest_WithoutTemperatureOrSeed_OmitsBothFromPayload()
    {
        var adapter = new OpenAiProviderAdapter();
        Assert.Equal(((double?)null, (int?)null), adapter.EffectiveSamplingParams);

        var request = adapter.CreateRequest(
            endpoint: "",
            apiKey: "sk-test",
            model: "gpt-4o-mini",
            systemPrompt: "be good",
            userPrompt: "do it");

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.False(root.TryGetProperty("seed", out _));
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

    /// <summary>N5: Anthropic has no deterministic-seed API parameter, so EffectiveSamplingParams.Seed
    /// is always null regardless of configuration - only Temperature is ever sent.</summary>
    [Fact]
    public async Task CreateRequest_WithTemperature_IncludesItAndNeverASeed()
    {
        var adapter = new AnthropicProviderAdapter(temperature: 0.2);
        Assert.Equal(((double?)0.2, (int?)null), adapter.EffectiveSamplingParams);

        var request = adapter.CreateRequest(
            endpoint: "",
            apiKey: "sk-ant-123",
            model: "claude-3-sonnet",
            systemPrompt: "sys",
            userPrompt: "user");

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal(0.2, root.GetProperty("temperature").GetDouble());
        Assert.False(root.TryGetProperty("seed", out _));
    }

    [Fact]
    public async Task CreateRequest_WithoutTemperature_OmitsItFromPayload()
    {
        var adapter = new AnthropicProviderAdapter();

        var request = adapter.CreateRequest(
            endpoint: "",
            apiKey: "sk-ant-123",
            model: "claude-3-sonnet",
            systemPrompt: "sys",
            userPrompt: "user");

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("temperature", out _));
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

    /// <summary>N5: temperature lands in generationConfig alongside the existing responseSchema;
    /// Gemini's generateContent (as this adapter targets it) has no equivalent to a deterministic
    /// seed, so EffectiveSamplingParams.Seed is always null.</summary>
    [Fact]
    public async Task CreateRequest_WithTemperature_AddsItToGenerationConfig()
    {
        var adapter = new GoogleProviderAdapter(temperature: 0.7);
        Assert.Equal(((double?)0.7, (int?)null), adapter.EffectiveSamplingParams);

        var request = adapter.CreateRequest(
            endpoint: "",
            apiKey: "goog-key",
            model: "gemini-1.5-flash",
            systemPrompt: "sys",
            userPrompt: "hi");

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var generationConfig = doc.RootElement.GetProperty("generationConfig");

        Assert.Equal(0.7, generationConfig.GetProperty("temperature").GetDouble());
        // Existing responseSchema behavior must be unaffected by adding temperature.
        Assert.True(generationConfig.TryGetProperty("responseSchema", out _));
    }

    [Fact]
    public async Task CreateRequest_WithoutTemperature_OmitsGenerationConfigTemperature()
    {
        var adapter = new GoogleProviderAdapter(useResponseSchema: false);

        var request = adapter.CreateRequest(
            endpoint: "",
            apiKey: "goog-key",
            model: "gemini-1.5-flash",
            systemPrompt: "sys",
            userPrompt: "hi");

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        // No temperature and no responseSchema -> generationConfig has nothing to carry, so it's
        // omitted entirely rather than sent as an empty object.
        Assert.False(doc.RootElement.TryGetProperty("generationConfig", out _));
    }
}
