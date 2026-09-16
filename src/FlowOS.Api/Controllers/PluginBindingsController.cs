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
        if (string.IsNullOrWhiteSpace(providerName))
            return BadRequest("providerName is required.");

        string? configurationJson = null;
        if (request.Configuration is { ValueKind: JsonValueKind.Object or JsonValueKind.String } config)
            configurationJson = config.ValueKind == JsonValueKind.String
                ? config.GetString()
                : config.GetRawText();

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
