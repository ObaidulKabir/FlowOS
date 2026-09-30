using System.Text.Json;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/plugin-bindings")]
[Authorize]
public class PluginBindingsController : ControllerBase
{
    private readonly IPluginBindingRegistryService _bindings;
    private readonly ICurrentUser _currentUser;

    public PluginBindingsController(IPluginBindingRegistryService bindings, ICurrentUser currentUser)
    {
        _bindings = bindings;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? bindingType,
        [FromQuery] string? sourceName,
        [FromQuery] bool? enabledOnly,
        CancellationToken cancellationToken)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");
        var items = await _bindings.ListAsync(
            _currentUser.TenantId,
            bindingType,
            sourceName,
            enabledOnly,
            cancellationToken);
        return Ok(new { totalCount = items.Count, bindings = items });
    }

    [HttpPut]
    public async Task<IActionResult> Upsert(
        [FromBody] UpsertPluginBindingRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");
        if (request == null || string.IsNullOrWhiteSpace(request.BindingType) || string.IsNullOrWhiteSpace(request.SourceName))
            return BadRequest("bindingType and sourceName are required.");

        var bindingType = request.BindingType.Trim().ToLowerInvariant();
        var providerName = request.ProviderName?.Trim();
        if (bindingType == PluginBindingTypes.Prompt && string.IsNullOrWhiteSpace(providerName))
            providerName = AgentPromptKinds.Markdown;

        string? configurationJson = null;
        if (request.Configuration is { ValueKind: JsonValueKind.Object or JsonValueKind.String } config)
            configurationJson = config.ValueKind == JsonValueKind.String
                ? config.GetString()
                : config.GetRawText();

        if (bindingType == PluginBindingTypes.Profile)
        {
            var parsedProfile = FlowOS.Core.Common.Models.AgentProfileConfiguration.Parse(configurationJson);
            if (string.IsNullOrWhiteSpace(providerName))
                providerName = parsedProfile?.Role?.Trim();
            if (string.IsNullOrWhiteSpace(providerName))
                providerName = request.SourceName.Trim();
            if (parsedProfile?.AutoCommitThreshold is < 0.0 or > 1.0)
                return BadRequest("autoCommitThreshold must be between 0.0 and 1.0.");
        }

        if (bindingType == PluginBindingTypes.Action)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return BadRequest("providerName is required for action tool bindings (e.g. LookupRecord, QueryRecords, connector:crm).");

            var parsedTool = FlowOS.Core.Common.Models.AgentToolConfiguration.Parse(configurationJson);
            if (parsedTool != null)
            {
                var sideEffect = parsedTool.SideEffect?.ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(sideEffect) &&
                    sideEffect is not ("none" or "read" or "write" or "notify"))
                {
                    return BadRequest("sideEffect must be one of 'none', 'read', 'write', or 'notify'.");
                }

                if (parsedTool.Prefetch && sideEffect is "write" or "notify")
                {
                    return BadRequest("Tools with sideEffect 'write' or 'notify' cannot be configured for prefetch.");
                }

                if (!string.IsNullOrWhiteSpace(parsedTool.ParametersSchema))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(parsedTool.ParametersSchema);
                    }
                    catch (JsonException)
                    {
                        return BadRequest("parametersSchema must be a valid JSON Schema string or object.");
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(providerName))
            return BadRequest("providerName is required.");

        if (bindingType == PluginBindingTypes.Prompt)
        {
            var parsed = FlowOS.Core.Common.Models.AgentPromptConfiguration.Parse(configurationJson);
            var existing = (await _bindings.ListAsync(
                _currentUser.TenantId, bindingType, request.SourceName, ct: cancellationToken))
                .FirstOrDefault();
            var existingPrompt = existing?.Configuration as FlowOS.Core.Common.Models.AgentPromptConfiguration;
            if ((parsed == null || string.IsNullOrWhiteSpace(parsed.Body)) &&
                (existingPrompt == null || string.IsNullOrWhiteSpace(existingPrompt.Body)))
            {
                return BadRequest("configuration.instructions is required when creating a prompt.");
            }
        }
        else if (bindingType == PluginBindingTypes.Agent)
        {
            if (!AgentProviderKinds.IsKnown(providerName))
            {
                return BadRequest(
                    $"Unknown providerName '{providerName}'. Use {AgentProviderKinds.OpenAi}, {AgentProviderKinds.Anthropic}, {AgentProviderKinds.AzureOpenAi}, {AgentProviderKinds.Google}, {AgentProviderKinds.Custom}, {AgentProviderKinds.FlowosHosted}, or {AgentProviderKinds.FlowosRisk}.");
            }

            var parsedAgent = FlowOS.Core.Common.Models.AgentProviderConfiguration.Parse(configurationJson);
            var existingAgent = (await _bindings.ListAsync(
                _currentUser.TenantId, bindingType, request.SourceName, ct: cancellationToken))
                .FirstOrDefault();
            var existingSettings = existingAgent?.Configuration as FlowOS.Core.Common.Models.AgentProviderPublicSettings;

            var normalizedProvider = providerName.ToLowerInvariant();
            if (normalizedProvider is AgentProviderKinds.AzureOpenAi or AgentProviderKinds.Custom)
            {
                var effectiveEndpoint = !string.IsNullOrWhiteSpace(parsedAgent?.Endpoint)
                    ? parsedAgent.Endpoint
                    : existingSettings?.Endpoint;

                if (string.IsNullOrWhiteSpace(effectiveEndpoint))
                {
                    return BadRequest($"endpoint is required for '{normalizedProvider}' provider.");
                }

                if (!Uri.TryCreate(effectiveEndpoint, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return BadRequest("endpoint must be a valid HTTP or HTTPS URL.");
                }
            }

            if (normalizedProvider == AgentProviderKinds.Custom)
            {
                var effectiveModel = !string.IsNullOrWhiteSpace(parsedAgent?.Model)
                    ? parsedAgent.Model
                    : existingSettings?.Model;

                if (string.IsNullOrWhiteSpace(effectiveModel))
                {
                    return BadRequest("model is required for 'custom' provider.");
                }
            }

            if (normalizedProvider is AgentProviderKinds.OpenAi or AgentProviderKinds.Anthropic or AgentProviderKinds.Google or AgentProviderKinds.AzureOpenAi)
            {
                var hasKey = !string.IsNullOrWhiteSpace(parsedAgent?.ApiKey) || (existingSettings?.HasApiKey == true);
                if (!hasKey)
                {
                    return BadRequest($"apiKey is required when configuring '{normalizedProvider}'.");
                }
            }
        }

        try
        {
            var binding = await _bindings.UpsertAsync(
                _currentUser.TenantId,
                bindingType,
                request.SourceName,
                providerName,
                request.IsEnabled,
                configurationJson,
                cancellationToken);
            return Ok(binding);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

public sealed class UpsertPluginBindingRequest
{
    public string BindingType { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string? ProviderName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public JsonElement? Configuration { get; set; }
}
