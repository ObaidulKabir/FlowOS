using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class WebhookSecurityMcpTools
{
    private readonly IWebhookSignatureService _signatureService;
    private readonly FlowOSDbContext _dbContext;

    public WebhookSecurityMcpTools(IWebhookSignatureService signatureService, FlowOSDbContext dbContext)
    {
        _signatureService = signatureService;
        _dbContext = dbContext;
    }

    public async Task<CallToolResult> VerifyWebhookSignature(JObject args)
    {
        try
        {
            var payloadToken = args["payload"];
            if (payloadToken == null)
            {
                return McpToolResults.Fail("MCP-ARG-001", "payload is required.");
            }

            var payload = payloadToken is JObject || payloadToken is JArray
                ? payloadToken.ToString(Formatting.None)
                : payloadToken.ToString();

            var secret = args["secret"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(secret))
            {
                var tenantId = ResolveOptionalTenant(args);
                if (tenantId.HasValue && tenantId.Value != Guid.Empty)
                {
                    var tenant = await _dbContext.Tenants.FindAsync(tenantId.Value);
                    secret = tenant?.WebhookSigningSecret;
                }

                if (string.IsNullOrWhiteSpace(secret))
                {
                    secret = "whsec_default_flowos_secret";
                }
            }

            var signature = args["signature"]?.ToString()?.Trim();
            var timestamp = args["timestamp"]?.Value<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (!string.IsNullOrWhiteSpace(signature))
            {
                var isValid = _signatureService.VerifySignature(secret, payload, signature);
                return McpToolResults.Success(new
                {
                    isValid = isValid,
                    signature = signature,
                    evaluatedAgainstPayloadLength = payload.Length,
                    message = isValid ? "Signature is valid and matches payload." : "Signature verification failed."
                });
            }
            else
            {
                var signatureHeader = _signatureService.FormatSignatureHeader(secret, payload, timestamp);
                var hash = _signatureService.ComputeSignature(secret, payload, timestamp);
                return McpToolResults.Success(new
                {
                    signatureHeader = signatureHeader,
                    timestamp = timestamp,
                    hash = hash,
                    algorithm = "HMAC-SHA256",
                    message = "Signature successfully generated."
                });
            }
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Webhook signature operation failed: {ex.Message}");
        }
    }

    public async Task<CallToolResult> TestWebhookEndpoint(JObject args)
    {
        try
        {
            var url = args["url"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return McpToolResults.Fail("MCP-ARG-001", "Valid absolute url is required.");
            }

            var method = args["method"]?.ToString()?.Trim()?.ToUpperInvariant() ?? "POST";
            var signPayload = args["signPayload"]?.Value<bool>() ?? true;

            var payloadToken = args["payload"];
            var rawPayload = payloadToken != null
                ? (payloadToken is JObject || payloadToken is JArray ? payloadToken.ToString(Formatting.None) : payloadToken.ToString())
                : JsonConvert.SerializeObject(new { test = true, pingAt = DateTime.UtcNow, source = "FlowOS MCP Diagnostic Ping" });

            var tenantId = ResolveOptionalTenant(args) ?? Guid.NewGuid();
            string signingSecret = "whsec_test_flowos_ping_secret";

            var tenant = await _dbContext.Tenants.FindAsync(tenantId);
            if (tenant != null && !string.IsNullOrWhiteSpace(tenant.WebhookSigningSecret))
            {
                signingSecret = tenant.WebhookSigningSecret;
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(new HttpMethod(method), uri);

            var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            request.Headers.Add("x-tenant-id", tenantId.ToString());
            request.Headers.Add("x-flowos-action", "test-ping");
            request.Headers.Add("X-FlowOS-Timestamp", nowSeconds.ToString());
            request.Headers.Add("X-FlowOS-Delivery", $"test-{Guid.NewGuid():N}");

            if (signPayload)
            {
                var sigHeader = _signatureService.FormatSignatureHeader(signingSecret, rawPayload, nowSeconds);
                request.Headers.Add("X-FlowOS-Signature", sigHeader);
            }

            request.Content = new StringContent(rawPayload, Encoding.UTF8, "application/json");

            // Attach any custom headers
            if (args["headers"] is JObject customHeaders)
            {
                foreach (var prop in customHeaders.Properties())
                {
                    var val = prop.Value?.ToString();
                    if (val != null && !request.Headers.TryAddWithoutValidation(prop.Name, val))
                    {
                        request.Content.Headers.TryAddWithoutValidation(prop.Name, val);
                    }
                }
            }

            var sw = Stopwatch.StartNew();
            var response = await client.SendAsync(request);
            sw.Stop();

            var responseBody = await response.Content.ReadAsStringAsync();
            if (responseBody.Length > 500)
            {
                responseBody = responseBody.Substring(0, 500) + "... [truncated]";
            }

            return McpToolResults.Success(new
            {
                targetUrl = url,
                httpMethod = method,
                statusCode = (int)response.StatusCode,
                statusText = response.StatusCode.ToString(),
                latencyMs = sw.ElapsedMilliseconds,
                isSuccess = response.IsSuccessStatusCode,
                signatureSent = signPayload,
                timestampSent = nowSeconds,
                responseSnippet = responseBody
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-EXEC-001", $"Failed to send test webhook: {ex.Message}");
        }
    }

    public async Task<CallToolResult> RotateWebhookSecret(JObject args)
    {
        try
        {
            var tenantId = ResolveOptionalTenant(args);
            if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            {
                return McpToolResults.Fail("MCP-ARG-001", "tenantId is required to rotate webhook secret.");
            }

            var tenant = await _dbContext.Tenants.FindAsync(tenantId.Value);
            if (tenant == null)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Tenant '{tenantId.Value}' not found.");
            }

            var newSecret = tenant.RotateWebhookSigningSecret();
            await _dbContext.SaveChangesAsync();

            return McpToolResults.Success(new
            {
                success = true,
                tenantId = tenantId.Value,
                webhookSigningSecret = newSecret,
                message = "Tenant webhook signing secret rotated successfully. Downstream consumers must update their verification secret."
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to rotate webhook secret: {ex.Message}");
        }
    }

    private static Guid? ResolveOptionalTenant(JObject args)
    {
        var tenantStr = args["tenantId"]?.ToString()?.Trim();
        if (!string.IsNullOrWhiteSpace(tenantStr) && Guid.TryParse(tenantStr, out var tid))
        {
            return tid;
        }

        if (McpRequestContext.TenantId != Guid.Empty)
        {
            return McpRequestContext.TenantId;
        }

        return null;
    }
}
