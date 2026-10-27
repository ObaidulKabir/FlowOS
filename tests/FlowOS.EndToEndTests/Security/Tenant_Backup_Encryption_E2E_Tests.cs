using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Security;

/// <summary>CR-15: the backup endpoints encrypt and decrypt with the caller's passphrase.</summary>
public class Tenant_Backup_Encryption_E2E_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Passphrase = "correct horse battery";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Tenant _tenant = new("Backup Encryption " + Guid.NewGuid().ToString("N")[..8]);

    public Tenant_Backup_Encryption_E2E_Tests(WebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_E2E_BackupEncryption_" + Guid.NewGuid();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FlowOS:Backup:RequireEncryption"] = "true"
            }));
            builder.ConfigureTestServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList()) services.Remove(d);
                foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList()) services.Remove(d);
                services.AddScoped<FlowOSDbContext>(_ => new TestFlowOSDbContext(
                    new DbContextOptionsBuilder<FlowOSDbContext>().UseInMemoryDatabase(dbName).Options));
            });
        });
    }

    private async Task<HttpClient> ClientAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            if (!await db.Tenants.AnyAsync(t => t.TenantId == _tenant.TenantId))
            {
                db.Tenants.Add(_tenant);
                await db.SaveChangesAsync();
            }
        }
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-tenant-id", _tenant.TenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
        return client;
    }

    private HttpRequestMessage Download(string? passphrase)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tenants/{_tenant.TenantId}/backup");
        request.Headers.Add(TenantBackupConfirmation.HeaderName, TenantBackupConfirmation.Download);
        if (passphrase != null) request.Headers.Add(TenantBackupEncryption.PassphraseHeader, passphrase);
        return request;
    }

    private HttpRequestMessage Restore(string body, string? passphrase)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tenants/{_tenant.TenantId}/backup/restore")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(TenantBackupConfirmation.HeaderName, TenantBackupConfirmation.Restore);
        if (passphrase != null) request.Headers.Add(TenantBackupEncryption.PassphraseHeader, passphrase);
        return request;
    }

    private static async Task<string> CodeOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Download_without_a_passphrase_is_refused_when_encryption_is_required()
    {
        var client = await ClientAsync();

        var response = await client.SendAsync(Download(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("BACKUP-PASSPHRASE-REQUIRED", await CodeOf(response));
    }

    [Fact]
    public async Task Short_passphrase_is_refused()
    {
        var client = await ClientAsync();

        var response = await client.SendAsync(Download("short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("BACKUP-PASSPHRASE-WEAK", await CodeOf(response));
    }

    [Fact]
    public async Task Download_is_encrypted_and_restore_needs_the_same_passphrase()
    {
        var client = await ClientAsync();

        var download = await client.SendAsync(Download(Passphrase));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        var file = await download.Content.ReadAsStringAsync();
        Assert.True(TenantBackupEncryption.IsEncrypted(file));
        Assert.DoesNotContain(_tenant.Name, file);
        Assert.Contains(".encrypted", download.Content.Headers.ContentDisposition?.FileName ?? string.Empty);

        var missing = await client.SendAsync(Restore(file, null));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("BACKUP-PASSPHRASE-REQUIRED", await CodeOf(missing));

        var wrong = await client.SendAsync(Restore(file, "wrong horse battery"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("BACKUP-DECRYPT-FAILED", await CodeOf(wrong));

        // The right passphrase decrypts it; restoring onto the same tenant is then refused as a conflict by
        // design (restore is for another site), which proves the file was decrypted and read.
        var right = await client.SendAsync(Restore(file, Passphrase));
        Assert.NotEqual("BACKUP-DECRYPT-FAILED", right.IsSuccessStatusCode ? null : await CodeOf(right));
    }
}
