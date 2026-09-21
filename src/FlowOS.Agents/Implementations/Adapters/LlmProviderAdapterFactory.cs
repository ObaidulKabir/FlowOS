namespace FlowOS.Agents.Implementations.Adapters;

public static class LlmProviderAdapterFactory
{
    private static readonly OpenAiProviderAdapter OpenAi = new(useStrictJsonSchema: true);
    private static readonly OpenAiProviderAdapter AzureOpenAi =
        new(useStrictJsonSchema: true, useApiKeyHeader: true);
    private static readonly OpenAiProviderAdapter OpenAiCompatible =
        new(useStrictJsonSchema: false);
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
            "azure-openai" => AzureOpenAi,
            "custom" => OpenAiCompatible,
            _ => OpenAi
        };
    }
}
