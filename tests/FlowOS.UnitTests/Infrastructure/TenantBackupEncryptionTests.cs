using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FlowOS.Core.Common.Models;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

/// <summary>CR-15: encrypted tenant backups.</summary>
public class TenantBackupEncryptionTests
{
    private const string Passphrase = "correct horse battery";
    private const string Plain = "{\"format\":\"flowos-tenant-backup\",\"formatVersion\":1,\"flowOsVersion\":\"1.2.2\",\"takenAtUtc\":\"2026-10-27T09:00:00Z\",\"pluginBindings\":[{\"apiKey\":\"sk-secret-123\"}]}";

    private static string Code(Action action) => Assert.Throws<TenantBackupException>(action).Code;

    [Fact]
    public void Round_trip_hides_the_contents_and_restores_them_exactly()
    {
        var encrypted = TenantBackupEncryption.Encrypt(Plain, Passphrase);

        Assert.DoesNotContain("sk-secret-123", encrypted);
        Assert.True(TenantBackupEncryption.IsEncrypted(encrypted));
        Assert.False(TenantBackupEncryption.IsEncrypted(Plain));
        Assert.Equal(Plain, TenantBackupEncryption.Decrypt(encrypted, Passphrase));
    }

    [Fact]
    public void Each_file_gets_a_fresh_salt_and_nonce()
    {
        var a = JsonNode.Parse(TenantBackupEncryption.Encrypt(Plain, Passphrase))!;
        var b = JsonNode.Parse(TenantBackupEncryption.Encrypt(Plain, Passphrase))!;

        Assert.NotEqual(a["salt"]!.ToString(), b["salt"]!.ToString());
        Assert.NotEqual(a["nonce"]!.ToString(), b["nonce"]!.ToString());
    }

    [Fact]
    public void Wrong_or_missing_passphrase_is_rejected()
    {
        var encrypted = TenantBackupEncryption.Encrypt(Plain, Passphrase);

        Assert.Equal("BACKUP-DECRYPT-FAILED", Code(() => TenantBackupEncryption.Decrypt(encrypted, "wrong horse battery")));
        Assert.Equal("BACKUP-PASSPHRASE-REQUIRED", Code(() => TenantBackupEncryption.Decrypt(encrypted, null)));
    }

    [Theory]
    [InlineData("flowOsVersion", "9.9.9")]
    [InlineData("takenAtUtc", "2020-01-01T00:00:00Z")]
    public void Changing_a_readable_header_field_breaks_decryption(string field, string value)
    {
        var envelope = JsonNode.Parse(TenantBackupEncryption.Encrypt(Plain, Passphrase))!;
        envelope[field] = value;

        Assert.Equal("BACKUP-DECRYPT-FAILED", Code(() => TenantBackupEncryption.Decrypt(envelope.ToJsonString(), Passphrase)));
    }

    [Fact]
    public void Changing_the_ciphertext_breaks_decryption()
    {
        var envelope = JsonNode.Parse(TenantBackupEncryption.Encrypt(Plain, Passphrase))!;
        var bytes = Convert.FromBase64String(envelope["ciphertext"]!.ToString());
        bytes[3] ^= 1;
        envelope["ciphertext"] = Convert.ToBase64String(bytes);

        Assert.Equal("BACKUP-DECRYPT-FAILED", Code(() => TenantBackupEncryption.Decrypt(envelope.ToJsonString(), Passphrase)));
    }

    [Fact]
    public void Crafted_or_damaged_files_are_refused_before_any_slow_work()
    {
        var envelope = JsonNode.Parse(TenantBackupEncryption.Encrypt(Plain, Passphrase))!;
        envelope["iterations"] = 2_000_000_000;
        Assert.Equal("BACKUP-FORMAT", Code(() => TenantBackupEncryption.Decrypt(envelope.ToJsonString(), Passphrase)));

        Assert.Equal("BACKUP-FORMAT", Code(() => TenantBackupEncryption.Decrypt(
            "{\"format\":\"flowos-tenant-backup-encrypted\",\"formatVersion\":1,\"cipher\":\"AES-256-GCM\",\"kdf\":\"PBKDF2-SHA256\"}", Passphrase)));
    }

    [Fact]
    public void Short_passphrases_are_refused()
    {
        Assert.Equal("BACKUP-PASSPHRASE-WEAK", Code(() => TenantBackupEncryption.Encrypt(Plain, "short")));
    }

    [Fact]
    public async Task Encrypted_export_restores_onto_another_site()
    {
        await using var source = Context();
        await using var destination = Context();
        var tenant = new Tenant("Encrypted Source Org");
        source.Tenants.Add(tenant);
        source.WorkflowClasses.Add(new WorkflowClass(tenant.TenantId, "Certification", "1.0.0", new WorkflowClassBlueprint()));
        var aiBinding = new PluginBindingRecord(tenant.TenantId, "prompt", "assessor", "markdown");
        aiBinding.Update("markdown", true, "{\"instructions\":\"private assessment rubric\"}");
        source.PluginBindings.Add(aiBinding);
        await source.SaveChangesAsync();
        var destinationTenant = new Tenant("Encrypted Destination Org");
        destination.Tenants.Add(destinationTenant);
        await destination.SaveChangesAsync();

        var exported = await new TenantBackupService(source).ExportJsonAsync(tenant.TenantId);
        var encrypted = TenantBackupEncryption.Encrypt(exported, Passphrase);
        Assert.DoesNotContain("private assessment rubric", encrypted);
        Assert.DoesNotContain("Encrypted Source Org", encrypted);

        var restored = await new TenantBackupService(destination)
            .RestoreJsonAsync(destinationTenant.TenantId, TenantBackupEncryption.Decrypt(encrypted, Passphrase));

        Assert.Equal(1, restored.WorkflowClasses);
        Assert.Equal(1, restored.PluginBindings);
    }

    private static FlowOSDbContext Context() =>
        new(new DbContextOptionsBuilder<FlowOSDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
