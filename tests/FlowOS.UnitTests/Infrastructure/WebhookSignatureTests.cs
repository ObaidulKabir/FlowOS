using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Services;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.MCP.Tools;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class WebhookSignatureTests
{
    private readonly WebhookSignatureService _service = new();
    private readonly string _testSecret = "whsec_0123456789abcdef0123456789abcdef";

    [Fact]
    public void WebhookSignature_Computes_Valid_HMACSHA256()
    {
        var payload = "{\"orderId\":\"ORD-999\",\"amount\":150.50}";
        long timestamp = 1700000000;

        var header = _service.FormatSignatureHeader(_testSecret, payload, timestamp);
        Assert.StartsWith("t=1700000000,v1=", header);

        var hash = _service.ComputeSignature(_testSecret, payload, timestamp);
        Assert.Equal(64, hash.Length); // 256 bits = 64 hex characters
        Assert.EndsWith(hash, header);
    }

    [Fact]
    public void WebhookSignature_Verifies_Valid_Signature()
    {
        var payload = "{\"status\":\"approved\"}";
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var header = _service.FormatSignatureHeader(_testSecret, payload, timestamp);
        var isValid = _service.VerifySignature(_testSecret, payload, header);
        Assert.True(isValid);
    }

    [Fact]
    public void WebhookSignature_Rejects_Tampered_Payload()
    {
        var payload = "{\"status\":\"approved\"}";
        var tamperedPayload = "{\"status\":\"rejected\"}";
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var header = _service.FormatSignatureHeader(_testSecret, payload, timestamp);
        var isValid = _service.VerifySignature(_testSecret, tamperedPayload, header);
        Assert.False(isValid);
    }

    [Fact]
    public void WebhookSignature_Rejects_Expired_Timestamp()
    {
        var payload = "{\"status\":\"approved\"}";
        // 10 minutes ago
        long oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();

        var header = _service.FormatSignatureHeader(_testSecret, payload, oldTimestamp);
        // Default tolerance is 5 minutes
        var isValid = _service.VerifySignature(_testSecret, payload, header);
        Assert.False(isValid);
    }

    [Fact]
    public void Tenant_Can_Rotate_WebhookSigningSecret()
    {
        var tenant = new Tenant("Acme Corp");
        var initialSecret = tenant.WebhookSigningSecret;
        Assert.NotNull(initialSecret);
        Assert.StartsWith("whsec_", initialSecret!);

        var rotatedSecret = tenant.RotateWebhookSigningSecret();
        Assert.NotNull(rotatedSecret);
        Assert.StartsWith("whsec_", rotatedSecret);
        Assert.NotEqual(initialSecret, rotatedSecret);
        Assert.Equal(rotatedSecret, tenant.WebhookSigningSecret);
    }

    [Fact]
    public async Task Dispatcher_ShouldInclude_Headers_And_Signature_Metadata()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var dispatcher = new WorkflowActionDispatcher(db);

        var actions = new List<StepActionDefinition>
        {
            new("Webhook")
            {
                Url = "https://api.example.com/notify",
                Method = "POST",
                SignPayload = true,
                Headers = new Dictionary<string, string>
                {
                    { "Authorization", "Bearer {{Token}}" },
                    { "X-Custom-Env", "Production" }
                }
            }
        };

        var payload = new Dictionary<string, object>
        {
            { "Token", "secret-token-abc" }
        };

        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await dispatcher.QueueActionsAsync(tenantId, instanceId, "Step1", "OnEntry", actions, payload, CancellationToken.None);
        await db.SaveChangesAsync();

        var outbox = await db.OutboxMessages.FirstOrDefaultAsync();
        Assert.NotNull(outbox);

        var doc = JObject.Parse(outbox!.Payload);
        Assert.True(doc["signPayload"]?.Value<bool>());
        Assert.Equal("Bearer secret-token-abc", doc["headers"]?["Authorization"]?.ToString());
        Assert.Equal("Production", doc["headers"]?["X-Custom-Env"]?.ToString());
    }

    [Fact]
    public async Task WebhookSecurityMcpTools_Should_Verify_And_Rotate_ViaMcp()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var tenant = new Tenant("Security Tenant");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var tools = new WebhookSecurityMcpTools(_service, db);

        // 1. Generate signature via MCP
        var generateArgs = new JObject
        {
            ["tenantId"] = tenant.TenantId.ToString(),
            ["payload"] = "{\"ping\":true}"
        };
        var genResult = await tools.VerifyWebhookSignature(generateArgs);
        Assert.False(genResult.IsError);
        var genData = JObject.Parse(genResult.Content[0].Text)["data"] as JObject;
        Assert.NotNull(genData);
        var sigHeader = genData!["signatureHeader"]?.ToString();
        Assert.NotNull(sigHeader);
        Assert.StartsWith("t=", sigHeader!);

        // 2. Verify signature via MCP
        var verifyArgs = new JObject
        {
            ["tenantId"] = tenant.TenantId.ToString(),
            ["payload"] = "{\"ping\":true}",
            ["signature"] = sigHeader
        };
        var verifyResult = await tools.VerifyWebhookSignature(verifyArgs);
        Assert.False(verifyResult.IsError);
        var verifyData = JObject.Parse(verifyResult.Content[0].Text)["data"] as JObject;
        Assert.True(verifyData!["isValid"]?.Value<bool>());

        // 3. Rotate secret via MCP
        var rotateArgs = new JObject
        {
            ["tenantId"] = tenant.TenantId.ToString()
        };
        var rotateResult = await tools.RotateWebhookSecret(rotateArgs);
        Assert.False(rotateResult.IsError);
        var rotateData = JObject.Parse(rotateResult.Content[0].Text)["data"] as JObject;
        Assert.True(rotateData!["success"]?.Value<bool>());
        var newSecret = rotateData!["webhookSigningSecret"]?.ToString();
        Assert.NotNull(newSecret);
        Assert.StartsWith("whsec_", newSecret!);
    }
}
