using System;
using System.Collections.Generic;
using FlowOS.Agents.Abstractions;
using FlowOS.Core.Common.Models;

namespace FlowOS.Agents.Implementations.Adapters;

public sealed class LlmProviderAdapterRegistry : ILlmProviderAdapterRegistry
{
    private readonly IEnumerable<ILlmProviderAdapterFactory> _factories;

    public LlmProviderAdapterRegistry(IEnumerable<ILlmProviderAdapterFactory> factories)
    {
        _factories = factories;
    }

    public ILlmProviderAdapter GetAdapter(string? providerName, AgentProviderConfiguration? config = null)
    {
        var normalized = string.IsNullOrWhiteSpace(providerName) ? "openai" : providerName.Trim().ToLowerInvariant();
        
        foreach (var factory in _factories)
        {
            if (factory.Supports(normalized))
            {
                return factory.Create(config);
            }
        }
        
        // Fallback to OpenAI if not found (legacy behavior)
        return new OpenAiProviderAdapter(useStrictJsonSchema: true);
    }
}

public interface ILlmProviderAdapterFactory
{
    bool Supports(string providerName);
    ILlmProviderAdapter Create(AgentProviderConfiguration? config);
}

public sealed class OpenAiAdapterFactory : ILlmProviderAdapterFactory
{
    public bool Supports(string providerName) => providerName == "openai";
    public ILlmProviderAdapter Create(AgentProviderConfiguration? config) => new OpenAiProviderAdapter(useStrictJsonSchema: true);
}

public sealed class AzureOpenAiAdapterFactory : ILlmProviderAdapterFactory
{
    public bool Supports(string providerName) => providerName == "azure-openai";
    public ILlmProviderAdapter Create(AgentProviderConfiguration? config) => new OpenAiProviderAdapter(useStrictJsonSchema: true, useApiKeyHeader: true);
}

public sealed class CustomOpenAiAdapterFactory : ILlmProviderAdapterFactory
{
    public bool Supports(string providerName) => providerName == "custom";
    public ILlmProviderAdapter Create(AgentProviderConfiguration? config) => new OpenAiProviderAdapter(useStrictJsonSchema: false);
}

public sealed class AnthropicAdapterFactory : ILlmProviderAdapterFactory
{
    public bool Supports(string providerName) => providerName == "anthropic";
    public ILlmProviderAdapter Create(AgentProviderConfiguration? config)
    {
        return config?.MaxTokens is { } maxTokens
            ? new AnthropicProviderAdapter(maxTokens)
            : new AnthropicProviderAdapter();
    }
}

public sealed class GoogleAdapterFactory : ILlmProviderAdapterFactory
{
    public bool Supports(string providerName) => providerName == "google";
    public ILlmProviderAdapter Create(AgentProviderConfiguration? config) => new GoogleProviderAdapter();
}
