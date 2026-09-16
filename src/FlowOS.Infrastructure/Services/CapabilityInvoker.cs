using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FlowOS.Infrastructure.Services;

public sealed class CapabilityInvoker : ICapabilityInvoker
{
    private readonly ICapabilityRegistryService _registry;
    private readonly IConfiguration _configuration;

    public CapabilityInvoker(
        ICapabilityRegistryService registry,
        IConfiguration configuration)
    {
        _registry = registry;
        _configuration = configuration;
    }

    public async Task<CapabilityInvokeResult> InvokeAsync(
        Guid tenantId,
        string capabilityName,
        string operation,
        object? payload,
        Guid? workflowInstanceId = null,
        string? stepId = null,
        CancellationToken cancellationToken = default)
    {
        var remoteEnabled = bool.TryParse(
            _configuration["FlowOS:Capabilities:EnableRemoteInvoke"],
            out var enabledFlag) && enabledFlag;
        if (!remoteEnabled)
        {
            return new CapabilityInvokeResult(
                false,
                null,
                null,
                null,
                "Remote capability invocation is disabled. Set FlowOS:Capabilities:EnableRemoteInvoke=true.");
        }

        var check = await _registry.ValidateBindingAsync(tenantId, capabilityName, cancellationToken);
        if (!check.IsValid || check.Binding == null)
        {
            return new CapabilityInvokeResult(
                false, null, null, null,
                $"Capability binding invalid for '{capabilityName}': {check.Message}");
        }

        var binding = check.Binding;
        var envelope = new
        {
            capability = capabilityName,
            operation,
            tenantId,
            workflowInstanceId,
            stepId,
            payload
        };

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(Math.Clamp(binding.TimeoutMs, 1000, 120000))
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, binding.EndpointUrl);
            request.Headers.TryAddWithoutValidation("x-tenant-id", tenantId.ToString());
            request.Headers.TryAddWithoutValidation("x-flowos-capability", capabilityName);
            request.Headers.TryAddWithoutValidation("x-flowos-operation", operation);
            if (!string.IsNullOrWhiteSpace(binding.AuthRef))
                request.Headers.TryAddWithoutValidation("x-flowos-auth-ref", binding.AuthRef);

            request.Content = new StringContent(
                JsonSerializer.Serialize(envelope),
                Encoding.UTF8,
                new MediaTypeHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            object? parsed = null;
            try
            {
                parsed = string.IsNullOrWhiteSpace(body)
                    ? null
                    : JsonSerializer.Deserialize<object>(body);
            }
            catch
            {
                parsed = body;
            }

            if (!response.IsSuccessStatusCode)
            {
                return new CapabilityInvokeResult(
                    false,
                    (int)response.StatusCode,
                    body,
                    parsed,
                    $"Capability '{capabilityName}' returned HTTP {(int)response.StatusCode}.");
            }

            return new CapabilityInvokeResult(true, (int)response.StatusCode, body, parsed, null);
        }
        catch (Exception ex)
        {
            return new CapabilityInvokeResult(false, null, null, null, ex.Message);
        }
    }
}
