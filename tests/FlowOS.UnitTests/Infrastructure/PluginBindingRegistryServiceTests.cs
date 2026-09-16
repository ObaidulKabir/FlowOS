using System;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class PluginBindingRegistryServiceTests
{
    [Fact]
    public async Task UpsertAndResolve_ActionBinding_ShouldPersistAndResolve()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var service = new PluginBindingRegistryService(db);
        var tenantId = Guid.NewGuid();

        await service.UpsertAsync(
            tenantId,
            PluginBindingTypes.Action,
            sourceName: "plugin:paymentRefunder",
            providerName: "InvokeCapability");

        var resolved = await service.ResolveProviderNameAsync(
            tenantId,
            PluginBindingTypes.Action,
            "plugin:paymentRefunder");

        Assert.Equal("InvokeCapability", resolved);
    }

    [Fact]
    public async Task Upsert_DisabledBinding_ShouldNotResolve()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var service = new PluginBindingRegistryService(db);
        var tenantId = Guid.NewGuid();

        await service.UpsertAsync(
            tenantId,
            PluginBindingTypes.Decision,
            sourceName: "risk-default",
            providerName: "risk-v2",
            isEnabled: false);

        var resolved = await service.ResolveProviderNameAsync(
            tenantId,
            PluginBindingTypes.Decision,
            "risk-default");

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveBindings_ShouldReturnCaseInsensitiveMap()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var service = new PluginBindingRegistryService(db);
        var tenantId = Guid.NewGuid();

        await service.UpsertAsync(tenantId, PluginBindingTypes.Decision, "risk-default", "risk-v2");
        var map = await service.ResolveBindingsAsync(tenantId, PluginBindingTypes.Decision);

        Assert.True(map.TryGetValue("RISK-DEFAULT", out var mapped));
        Assert.Equal("risk-v2", mapped);
    }

    [Fact]
    public async Task Upsert_AgentBinding_MergesApiKeyAndNeverReturnsIt()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var service = new PluginBindingRegistryService(db);
        var tenantId = Guid.NewGuid();

        await service.UpsertAsync(
            tenantId,
            PluginBindingTypes.Agent,
            sourceName: "quote-llm",
            providerName: "openai",
            configurationJson: """{"model":"gpt-4o-mini","endpoint":"https://api.openai.com/v1","apiKey":"sk-test-keep"}""");

        var listed = await service.ListAsync(tenantId, PluginBindingTypes.Agent);
        var dto = Assert.Single(listed);
        var settings = Assert.IsType<AgentProviderPublicSettings>(dto.Configuration);
        Assert.Equal("gpt-4o-mini", settings.Model);
        Assert.Equal("https://api.openai.com/v1", settings.Endpoint);
        Assert.True(settings.HasApiKey);
        Assert.DoesNotContain("sk-test-keep", System.Text.Json.JsonSerializer.Serialize(dto.Configuration));

        await service.UpsertAsync(
            tenantId,
            PluginBindingTypes.Agent,
            sourceName: "quote-llm",
            providerName: "openai",
            configurationJson: """{"model":"gpt-4o","apiKey":""}""");

        var updated = await service.GetEnabledAsync(tenantId, PluginBindingTypes.Agent, "quote-llm");
        settings = Assert.IsType<AgentProviderPublicSettings>(updated!.Configuration);
        Assert.Equal("gpt-4o", settings.Model);
        Assert.True(settings.HasApiKey);

        var stored = await db.PluginBindings.AsNoTracking().FirstAsync();
        Assert.Contains("sk-test-keep", stored.ConfigurationJson);
        Assert.Contains("\"model\":\"gpt-4o\"", stored.ConfigurationJson);
        Assert.DoesNotContain("gpt-4o-mini", stored.ConfigurationJson);

        var secrets = await service.GetAgentSecretsAsync(tenantId, "quote-llm");
        Assert.Equal("sk-test-keep", secrets!.ApiKey);
        Assert.Equal("gpt-4o", secrets.Model);
    }

    [Fact]
    public async Task Upsert_PromptBinding_CreatesAndEditsInstructions()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var service = new PluginBindingRegistryService(db);
        var tenantId = Guid.NewGuid();

        await service.UpsertAsync(
            tenantId,
            PluginBindingTypes.Prompt,
            sourceName: "quote-approval",
            providerName: "markdown",
            configurationJson: """{"title":"Quote approval","system":"Be precise.","instructions":"Approve within 15%."}""");

        var listed = await service.ListAsync(tenantId, PluginBindingTypes.Prompt);
        var dto = Assert.Single(listed);
        var prompt = Assert.IsType<AgentPromptConfiguration>(dto.Configuration);
        Assert.Equal("Quote approval", prompt.Title);
        Assert.Equal("Approve within 15%.", prompt.Body);

        await service.UpsertAsync(
            tenantId,
            PluginBindingTypes.Prompt,
            sourceName: "quote-approval",
            providerName: "markdown",
            configurationJson: """{"instructions":"Request revision if labor hours are missing."}""");

        var updated = await service.GetEnabledAsync(tenantId, PluginBindingTypes.Prompt, "quote-approval");
        prompt = Assert.IsType<AgentPromptConfiguration>(updated!.Configuration);
        Assert.Equal("Quote approval", prompt.Title);
        Assert.Equal("Be precise.", prompt.System);
        Assert.Equal("Request revision if labor hours are missing.", prompt.Body);
    }
}
