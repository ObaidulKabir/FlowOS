using System;
using System.IO;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

/// <summary>
/// The API host and the MCP host are separate containers, both recreated on every deploy. A BYO LLM key
/// encrypted by one must stay readable by the other and after a redeploy, and a key that can no longer
/// be decrypted must never be sent to the provider as if it were the key.
/// </summary>
public sealed class AgentSecretDataProtectionTests : IDisposable
{
    private readonly string _keysDirectory =
        Path.Combine(Path.GetTempPath(), "flowos-dp-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_keysDirectory))
            Directory.Delete(_keysDirectory, recursive: true);
    }

    [Fact]
    public async Task KeyEncryptedByOneHost_IsReadableByAnotherHostSharingThePersistedKeyRing()
    {
        using var db = NewDb();
        var tenantId = Guid.NewGuid();

        // "API host" and "MCP host": separate containers, separate service providers, one shared key directory.
        using var apiHost = BuildHost(_keysDirectory);
        using var mcpHost = BuildHost(_keysDirectory);

        var writer = new PluginBindingRegistryService(db, apiHost.GetRequiredService<IDataProtectionProvider>());
        await writer.UpsertAsync(
            tenantId,
            PluginBindingTypes.Agent,
            sourceName: "quote-llm",
            providerName: "openai",
            configurationJson: """{"model":"gpt-4o-mini","apiKey":"sk-shared-ring-key"}""");

        // Encrypted at rest, not plaintext.
        var stored = await db.PluginBindings.AsNoTracking().SingleAsync(x => x.SourceName == "quote-llm");
        var storedKey = AgentProviderConfiguration.Parse(stored.ConfigurationJson)!.ApiKey!;
        Assert.StartsWith("CfDJ8", storedKey);
        Assert.DoesNotContain("sk-shared-ring-key", stored.ConfigurationJson);

        var reader = new PluginBindingRegistryService(db, mcpHost.GetRequiredService<IDataProtectionProvider>());
        var secrets = await reader.GetAgentSecretsAsync(tenantId, "quote-llm");
        Assert.Equal("sk-shared-ring-key", secrets!.ApiKey);
    }

    [Fact]
    public async Task KeyRingPersistedOnDisk_SurvivesAHostRestart()
    {
        using var db = NewDb();
        var tenantId = Guid.NewGuid();

        using (var beforeRedeploy = BuildHost(_keysDirectory))
        {
            var writer = new PluginBindingRegistryService(db, beforeRedeploy.GetRequiredService<IDataProtectionProvider>());
            await writer.UpsertAsync(
                tenantId,
                PluginBindingTypes.Agent,
                sourceName: "quote-llm",
                providerName: "openai",
                configurationJson: """{"apiKey":"sk-survives-redeploy"}""");
        }

        using var afterRedeploy = BuildHost(_keysDirectory);
        var reader = new PluginBindingRegistryService(db, afterRedeploy.GetRequiredService<IDataProtectionProvider>());
        var secrets = await reader.GetAgentSecretsAsync(tenantId, "quote-llm");
        Assert.Equal("sk-survives-redeploy", secrets!.ApiKey);
    }

    [Fact]
    public async Task UndecryptableKey_IsReturnedAsMissing_NotAsCiphertext()
    {
        // Before this fix, a key encrypted with a key ring that was later lost (container recreated) fell
        // back to the raw ciphertext, which was then sent to the provider as the Bearer token.
        using var db = NewDb();
        var tenantId = Guid.NewGuid();

        var writer = new PluginBindingRegistryService(db, new EphemeralDataProtectionProvider());
        await writer.UpsertAsync(
            tenantId,
            PluginBindingTypes.Agent,
            sourceName: "quote-llm",
            providerName: "openai",
            configurationJson: """{"apiKey":"sk-lost-ring-key"}""");

        var readerWithDifferentRing = new PluginBindingRegistryService(db, new EphemeralDataProtectionProvider());
        var secrets = await readerWithDifferentRing.GetAgentSecretsAsync(tenantId, "quote-llm");
        Assert.NotNull(secrets);
        Assert.Null(secrets!.ApiKey);

        var readerWithoutProtector = new PluginBindingRegistryService(db);
        var secretsWithoutProtector = await readerWithoutProtector.GetAgentSecretsAsync(tenantId, "quote-llm");
        Assert.Null(secretsWithoutProtector!.ApiKey);
    }

    [Fact]
    public async Task LegacyPlaintextKey_IsStillUsable_WhenAProtectorIsConfigured()
    {
        // Keys stored before encryption was enabled (or by a host without Data Protection) must keep working.
        using var db = NewDb();
        var tenantId = Guid.NewGuid();

        var legacy = new PluginBindingRecord(tenantId, PluginBindingTypes.Agent, "legacy-llm", "openai");
        legacy.Update("openai", isEnabled: true, configurationJson: """{"apiKey":"sk-legacy-plaintext"}""");
        db.PluginBindings.Add(legacy);
        await db.SaveChangesAsync();

        var service = new PluginBindingRegistryService(db, new EphemeralDataProtectionProvider());
        var secrets = await service.GetAgentSecretsAsync(tenantId, "legacy-llm");
        Assert.Equal("sk-legacy-plaintext", secrets!.ApiKey);
    }

    [Fact]
    public async Task Backfill_SkipsUndecryptableKey_InsteadOfFingerprintingCiphertext()
    {
        using var db = NewDb();
        var tenantId = Guid.NewGuid();

        // A key encrypted with a key ring that is no longer available, stored before fingerprints existed.
        var lostRing = new EphemeralDataProtectionProvider().CreateProtector("FlowOS.AgentSecrets");
        var record = new PluginBindingRecord(tenantId, PluginBindingTypes.Agent, "quote-llm", "openai");
        record.Update(
            "openai",
            isEnabled: true,
            configurationJson: $$"""{"apiKey":"{{lostRing.Protect("sk-unrecoverable")}}"}""");
        db.PluginBindings.Add(record);
        await db.SaveChangesAsync();

        var service = new PluginBindingRegistryService(db, new EphemeralDataProtectionProvider());
        var backfilled = await service.BackfillAgentProviderKeyMetadataAsync(tenantId);

        Assert.Equal(0, backfilled);
        var after = await db.PluginBindings.AsNoTracking().SingleAsync(x => x.SourceName == "quote-llm");
        Assert.Null(AgentProviderConfiguration.Parse(after.ConfigurationJson)!.ApiKeyFingerprint);
    }

    private static FlowOSDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ServiceProvider BuildHost(string keysDirectory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlowOsDataProtection(keysDirectory);
        return services.BuildServiceProvider();
    }
}
