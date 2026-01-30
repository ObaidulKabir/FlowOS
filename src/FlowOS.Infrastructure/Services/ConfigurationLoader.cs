using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.Services;

public class ConfigurationLoader
{
    private readonly FlowOSDbContext _context;
    private readonly ILogger<ConfigurationLoader> _logger;
    private readonly string _configRoot;

    public ConfigurationLoader(FlowOSDbContext context, ILogger<ConfigurationLoader> logger, string configRoot)
    {
        _context = context;
        _logger = logger;
        _configRoot = configRoot;
    }

    public async Task LoadAllAsync(Guid tenantId)
    {
        await LoadEventsAsync(tenantId);
        await LoadStateMachinesAsync(tenantId);
        await LoadWorkflowsAsync(tenantId);
    }

    private async Task LoadEventsAsync(Guid tenantId)
    {
        var path = Path.Combine(_configRoot, "events");
        if (!Directory.Exists(path)) return;

        foreach (var file in Directory.GetFiles(path, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file);
            var dto = JsonSerializer.Deserialize<EventConfigDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (dto == null) continue;

            // Validate
            if (string.IsNullOrWhiteSpace(dto.EventId)) throw new InvalidDataException($"EventId missing in {file}");

            // Check existence
            var exists = await _context.EventDefinitions.AnyAsync(e => e.EventId == dto.EventId && e.TenantId == tenantId);
            if (!exists)
            {
                var evt = new EventDefinition(
                    dto.EventId,
                    tenantId,
                    dto.Name,
                    dto.Description,
                    dto.EntityType,
                    Enum.Parse<EventCategory>(dto.Category),
                    1, // Versioning for events could be added later
                    null,
                    dto.IsTerminal
                );
                
                // Explicitly Publish
                evt.Publish();

                _context.EventDefinitions.Add(evt);
                _logger.LogInformation($"Registered and Published event: {dto.EventId}");
            }
        }
        await _context.SaveChangesAsync();
    }

    private async Task LoadStateMachinesAsync(Guid tenantId)
    {
        var path = Path.Combine(_configRoot, "state-machines");
        if (!Directory.Exists(path)) return;

        foreach (var file in Directory.GetFiles(path, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file);
            var dto = JsonSerializer.Deserialize<StateMachineConfigDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (dto == null) continue;

            // Check if this version exists
            var exists = await _context.StateMachineDefinitions.AnyAsync(s => s.EntityType == dto.EntityType && s.Version == dto.Version && s.TenantId == tenantId);
            if (!exists)
            {
                var sm = new StateMachineDefinition(tenantId, dto.EntityType, dto.InitialState, dto.Version);
                
                foreach (var s in dto.States) sm.AddState(s);
                foreach (var t in dto.Transitions)
                {
                    // Validate Event ID against Registry
                    var eventExists = await _context.EventDefinitions.AnyAsync(e => e.EventId == t.EventId && e.TenantId == tenantId);
                    if (!eventExists && t.EventId.StartsWith("EVT-")) 
                    {
                         _logger.LogWarning($"StateMachine {dto.EntityType} references unknown event {t.EventId}");
                         // In strict mode, throw. For now, log.
                    }

                    sm.AddTransition(new Domain.ValueObjects.StateTransition(t.FromState, t.ToState, t.EventId) 
                    {
                        EventId = t.EventId
                    });
                }
                
                sm.Publish();
                _context.StateMachineDefinitions.Add(sm);
                _logger.LogInformation($"Published StateMachine: {dto.EntityType} v{dto.Version}");
            }
        }
        await _context.SaveChangesAsync();
    }
    
    private async Task LoadWorkflowsAsync(Guid tenantId)
    {
        // Placeholder for Workflow Loading logic (similar structure)
        // Ideally parses JSON to WorkflowDefinition entity
    }

    // DTOs for JSON deserialization
    private class EventConfigDto
    {
        public string EventId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string Category { get; set; } = "System";
        public bool IsTerminal { get; set; }
    }

    private class StateMachineConfigDto
    {
        public string EntityType { get; set; } = string.Empty;
        public int Version { get; set; }
        public string InitialState { get; set; } = string.Empty;
        public string[] States { get; set; } = Array.Empty<string>();
        public TransitionDto[] Transitions { get; set; } = Array.Empty<TransitionDto>();
    }

    private class TransitionDto
    {
        public string FromState { get; set; } = string.Empty;
        public string ToState { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
    }
}
