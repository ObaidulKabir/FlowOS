using System.Text.Json;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Interfaces;
using FlowOS.Core.Security;
using FlowOS.Security.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/plugin-bindings")]
[Authorize]
public class PluginBindingsController : ControllerBase
{
    private readonly IPluginBindingRegistryService _bindings;
    private readonly ICurrentUser _currentUser;
    private readonly ICapabilityService _capabilityService;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public PluginBindingsController(
        IPluginBindingRegistryService bindings,
        ICurrentUser currentUser,
        ICapabilityService capabilityService,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _bindings = bindings;
        _currentUser = currentUser;
        _capabilityService = capabilityService;
        _environment = environment;
        _configuration = configuration;
    }

    private bool IsEndpointSchemeAllowed(string scheme)
    {
        if (string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return false;

        return TenantIdentityRules.AllowInsecureAgentEndpoints(
            _environment.EnvironmentName,
            _configuration[TenantIdentityRules.AllowInsecureAgentEndpointsKey]);
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? bindingType,
        [FromQuery] string? sourceName,
        [FromQuery] bool? enabledOnly,
        CancellationToken cancellationToken,
        [FromQuery] Guid? applicationId = null,
        [FromQuery] bool? shared = null)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");
        IReadOnlyList<PluginBindingDto> items = await _bindings.ListAsync(
            _currentUser.TenantId,
            bindingType,
            sourceName,
            enabledOnly,
            cancellationToken);
        // Applications (3/4): applicationId lists only that application's prompts/profiles;
        // shared=true only tenant-shared bindings. Each binding carries applicationId either way.
        if (applicationId.HasValue && applicationId.Value != Guid.Empty)
            items = items.Where(b => b.ApplicationId == applicationId).ToList();
        else if (shared == true)
            items = items.Where(b => b.ApplicationId == null).ToList();
        return Ok(new { totalCount = items.Count, bindings = items });
    }

    /// <summary>
    /// Applications (3/4): deletes a prompt or agent profile binding in exactly one scope - an
    /// application's (applicationId) or tenant-shared (no applicationId). AI providers are deleted with
    /// the MCP tool delete_agent_provider, which enforces agent.manage.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(
        [FromQuery] string bindingType,
        [FromQuery] string sourceName,
        [FromQuery] Guid? applicationId,
        CancellationToken cancellationToken)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");
        var normalizedType = bindingType?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedType) || string.IsNullOrWhiteSpace(sourceName))
            return BadRequest("bindingType and sourceName are required.");
        if (normalizedType != PluginBindingTypes.Prompt && normalizedType != PluginBindingTypes.Profile)
            return BadRequest("Only prompt and profile bindings can be deleted here.");

        var deleted = await _bindings.DeleteScopedAsync(
            _currentUser.TenantId,
            applicationId == Guid.Empty ? null : applicationId,
            normalizedType,
            sourceName,
            cancellationToken);
        return deleted ? NoContent() : NotFound();
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
                .FirstOrDefault(b => b.ApplicationId == PluginBindingRequestScope.Of(request));
            var existingPrompt = existing?.Configuration as FlowOS.Core.Common.Models.AgentPromptConfiguration;
            if ((parsed == null || string.IsNullOrWhiteSpace(parsed.Body)) &&
                (existingPrompt == null || string.IsNullOrWhiteSpace(existingPrompt.Body)))
            {
                return BadRequest("configuration.instructions is required when creating a prompt.");
            }
        }
        else if (bindingType == PluginBindingTypes.Agent)
        {
            if (!await TenantCapabilityGate.HasCapabilityAsync(
                    _currentUser, _capabilityService, _currentUser.TenantId, AgentProviderKinds.ManageCapability))
            {
                return Forbid();
            }

            if (!AgentProviderKinds.IsKnown(providerName))
            {
                return BadRequest(
                    $"Unknown providerName '{providerName}'. Use {AgentProviderKinds.OpenAi}, {AgentProviderKinds.Anthropic}, {AgentProviderKinds.AzureOpenAi}, {AgentProviderKinds.Google}, {AgentProviderKinds.Custom}, {AgentProviderKinds.FlowosHosted}, or {AgentProviderKinds.FlowosRisk}.");
            }

            if (AgentProviderKinds.IsReservedAlias(request.SourceName) && !AgentProviderKinds.IsFlowOsHosted(providerName))
            {
                return BadRequest(
                    "The alias 'flowos-hosted' is platform-reserved: a step whose agentProvider resolves to it always gets the platform's hosted lease at runtime, regardless of what is saved here. Use a different alias for a BYO providerName key.");
            }

            var parsedAgent = FlowOS.Core.Common.Models.AgentProviderConfiguration.Parse(configurationJson);
            var existingAgent = (await _bindings.ListAsync(
                _currentUser.TenantId, bindingType, request.SourceName, ct: cancellationToken))
                .FirstOrDefault();
            var existingSettings = existingAgent?.Configuration as FlowOS.Core.Common.Models.AgentProviderPublicSettings;

            var normalizedProvider = providerName.ToLowerInvariant();

            // Any supplied endpoint must be a well-formed http(s) URL, and https:// outside Development,
            // for every BYO provider kind - not just the ones that require an endpoint. A BYO API key is
            // sent to this endpoint as a Bearer token.
            if (normalizedProvider != AgentProviderKinds.FlowosHosted &&
                !string.IsNullOrWhiteSpace(parsedAgent?.Endpoint))
            {
                if (!Uri.TryCreate(parsedAgent.Endpoint, UriKind.Absolute, out var suppliedUri) ||
                    (suppliedUri.Scheme != Uri.UriSchemeHttp && suppliedUri.Scheme != Uri.UriSchemeHttps))
                {
                    return BadRequest("endpoint must be a valid HTTP or HTTPS URL.");
                }

                if (!IsEndpointSchemeAllowed(suppliedUri.Scheme))
                {
                    return BadRequest("endpoint must be https:// outside Development - a BYO API key is sent to it as a Bearer token.");
                }
            }

            if (normalizedProvider is AgentProviderKinds.AzureOpenAi or AgentProviderKinds.Custom)
            {
                var effectiveEndpoint = !string.IsNullOrWhiteSpace(parsedAgent?.Endpoint)
                    ? parsedAgent.Endpoint
                    : existingSettings?.Endpoint;

                if (string.IsNullOrWhiteSpace(effectiveEndpoint))
                {
                    return BadRequest($"endpoint is required for '{normalizedProvider}' provider.");
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
            var applicationId = PluginBindingRequestScope.Of(request);
            var binding = applicationId.HasValue
                ? await _bindings.UpsertScopedAsync(
                    _currentUser.TenantId,
                    applicationId,
                    bindingType,
                    request.SourceName,
                    providerName,
                    request.IsEnabled,
                    configurationJson,
                    cancellationToken)
                : await _bindings.UpsertAsync(
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

internal static class PluginBindingRequestScope
{
    public static Guid? Of(UpsertPluginBindingRequest request) =>
        request.ApplicationId == Guid.Empty ? null : request.ApplicationId;
}

public sealed class UpsertPluginBindingRequest
{
    /// <summary>
    /// Applications (3/4): set for a prompt or profile that belongs to a tenant application; omit for a
    /// tenant-shared binding. Any other binding type with an applicationId is rejected.
    /// </summary>
    public Guid? ApplicationId { get; set; }

    public string BindingType { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string? ProviderName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public JsonElement? Configuration { get; set; }
}
