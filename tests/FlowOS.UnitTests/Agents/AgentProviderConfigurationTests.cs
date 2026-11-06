using System;
using System.Text.Json;
using FlowOS.Core.Common.Models;
using Xunit;

namespace FlowOS.UnitTests.Agents;

/// <summary>
/// Tenant integration finding #1 (endpoint-swap key exfiltration): changing only the <c>endpoint</c>
/// field previously kept the stored BYO API key unchanged, silently sending it to whatever new host
/// was saved - the key is used as a Bearer token against that endpoint. <see cref="AgentProviderConfiguration.Merge"/>
/// must now clear the stored key whenever the endpoint's host actually changes without a new key being
/// supplied in the same call, while leaving the key alone for same-host edits (path/scheme-only changes)
/// and for the normal case where a new key accompanies the new endpoint.
/// </summary>
public sealed class AgentProviderConfigurationTests
{
    private static string Json(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void Merge_EndpointHostChangedWithoutNewKey_ClearsStoredKey()
    {
        var existing = Json(new
        {
            model = "gpt-4o-mini",
            endpoint = "https://api.openai.com/v1/chat/completions",
            apiKey = "sk-original-key",
            apiKeyFingerprint = AgentProviderConfiguration.ComputeFingerprint("sk-original-key"),
            apiKeyUpdatedAtUtc = DateTime.UtcNow
        });

        var merged = AgentProviderConfiguration.Merge(
            existing,
            Json(new { endpoint = "https://attacker.example.com/v1/chat/completions" }));

        var result = AgentProviderConfiguration.Parse(merged)!;
        Assert.Equal("https://attacker.example.com/v1/chat/completions", result.Endpoint);
        Assert.Null(result.ApiKey);
        Assert.Null(result.ApiKeyFingerprint);
        Assert.Null(result.ApiKeyUpdatedAtUtc);
    }

    [Fact]
    public void Merge_EndpointPathOnlyChangeSameHost_KeepsStoredKey()
    {
        var existing = Json(new
        {
            model = "gpt-4o-mini",
            endpoint = "https://api.openai.com/v1/chat/completions",
            apiKey = "sk-original-key",
            apiKeyFingerprint = AgentProviderConfiguration.ComputeFingerprint("sk-original-key")
        });

        var merged = AgentProviderConfiguration.Merge(
            existing,
            Json(new { endpoint = "https://api.openai.com/v2/chat/completions" }));

        var result = AgentProviderConfiguration.Parse(merged)!;
        Assert.Equal("https://api.openai.com/v2/chat/completions", result.Endpoint);
        Assert.Equal("sk-original-key", result.ApiKey);
        Assert.NotNull(result.ApiKeyFingerprint);
    }

    [Fact]
    public void Merge_EndpointSchemeOnlyChangeSameHost_KeepsStoredKey()
    {
        var existing = Json(new
        {
            endpoint = "http://api.example.com/v1/chat/completions",
            apiKey = "sk-original-key"
        });

        var merged = AgentProviderConfiguration.Merge(
            existing,
            Json(new { endpoint = "https://api.example.com/v1/chat/completions" }));

        var result = AgentProviderConfiguration.Parse(merged)!;
        Assert.Equal("https://api.example.com/v1/chat/completions", result.Endpoint);
        Assert.Equal("sk-original-key", result.ApiKey);
    }

    [Fact]
    public void Merge_EndpointChangedWithNewKeySupplied_UsesNewKeyAndDoesNotClear()
    {
        var existing = Json(new
        {
            endpoint = "https://api.openai.com/v1/chat/completions",
            apiKey = "sk-original-key",
            apiKeyFingerprint = AgentProviderConfiguration.ComputeFingerprint("sk-original-key")
        });

        var merged = AgentProviderConfiguration.Merge(
            existing,
            Json(new { endpoint = "https://new-host.example.com/v1/chat/completions", apiKey = "sk-new-key" }));

        var result = AgentProviderConfiguration.Parse(merged)!;
        Assert.Equal("https://new-host.example.com/v1/chat/completions", result.Endpoint);
        Assert.Equal("sk-new-key", result.ApiKey);
        Assert.Equal(AgentProviderConfiguration.ComputeFingerprint("sk-new-key"), result.ApiKeyFingerprint);
    }

    [Fact]
    public void Merge_NoPreviousEndpoint_SettingFirstEndpointKeepsKey()
    {
        var existing = Json(new { apiKey = "sk-original-key" });

        var merged = AgentProviderConfiguration.Merge(
            existing,
            Json(new { endpoint = "https://api.openai.com/v1/chat/completions" }));

        var result = AgentProviderConfiguration.Parse(merged)!;
        Assert.Equal("https://api.openai.com/v1/chat/completions", result.Endpoint);
        Assert.Equal("sk-original-key", result.ApiKey);
    }

    [Fact]
    public void Merge_UnparseableNewEndpoint_TreatedAsHostChangeAndClearsKey()
    {
        var existing = Json(new
        {
            endpoint = "https://api.openai.com/v1/chat/completions",
            apiKey = "sk-original-key"
        });

        var merged = AgentProviderConfiguration.Merge(
            existing,
            Json(new { endpoint = "not-a-url" }));

        var result = AgentProviderConfiguration.Parse(merged)!;
        Assert.Equal("not-a-url", result.Endpoint);
        Assert.Null(result.ApiKey);
    }

    [Fact]
    public void Merge_EndpointUnchanged_KeepsStoredKey()
    {
        var existing = Json(new
        {
            endpoint = "https://api.openai.com/v1/chat/completions",
            apiKey = "sk-original-key"
        });

        var merged = AgentProviderConfiguration.Merge(
            existing,
            Json(new { model = "gpt-4o" }));

        var result = AgentProviderConfiguration.Parse(merged)!;
        Assert.Equal("https://api.openai.com/v1/chat/completions", result.Endpoint);
        Assert.Equal("sk-original-key", result.ApiKey);
    }
}
