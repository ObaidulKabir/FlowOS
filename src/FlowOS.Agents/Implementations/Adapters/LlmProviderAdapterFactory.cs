namespace FlowOS.Agents.Implementations.Adapters;

public static class LlmProviderAdapterFactory
{
    private static readonly OpenAiProviderAdapter OpenAi = new();
    private static readonly AnthropicProviderAdapter Anthropic = new();
    private static readonly GoogleProviderAdapter Google = new();

    public static ILlmProviderAdapter GetAdapter(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return OpenAi;

        var normalized = providerName.Trim().ToLowerInvariant();
        return normalized switch
        {
            "anthropic" => Anthropic,
            "google" => Google,
            _ => OpenAi
        };
    }
}
