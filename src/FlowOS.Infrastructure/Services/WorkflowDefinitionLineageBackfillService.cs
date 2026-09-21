using FlowOS.Application.Services;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.ValueObjects;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.Services;

public sealed record WorkflowDefinitionLineageBackfillResult(
    int DefinitionsScanned,
    int DefinitionsRepaired,
    int StateMachinesCreated,
    int DefinitionsSkipped);

/// <summary>
/// Repairs ordinary runtime definitions created before WorkflowClass compilation pinned Work and
/// Law together. Context-materialized definitions are deliberately excluded.
/// </summary>
public sealed class WorkflowDefinitionLineageBackfillService
{
    private readonly FlowOSDbContext _context;
    private readonly ILogger<WorkflowDefinitionLineageBackfillService> _logger;

    public WorkflowDefinitionLineageBackfillService(
        FlowOSDbContext context,
        ILogger<WorkflowDefinitionLineageBackfillService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<WorkflowDefinitionLineageBackfillResult> BackfillAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions = await _context.WorkflowDefinitions
            .Where(definition => definition.ContextBindingRevisionId == null)
            .ToListAsync(cancellationToken);
        var workflowClasses = await _context.WorkflowClasses
            .Where(workflowClass =>
                workflowClass.Status != WorkflowClassStatus.Draft &&
                workflowClass.Status != WorkflowClassStatus.Abandoned)
            .ToListAsync(cancellationToken);

        var repaired = 0;
        var stateMachinesCreated = 0;
        var skipped = 0;

        foreach (var definition in definitions)
        {
            WorkflowClass? source;
            if (definition.SourceWorkflowClassId.HasValue)
            {
                source = workflowClasses.FirstOrDefault(workflowClass =>
                    workflowClass.Id == definition.SourceWorkflowClassId.Value);
            }
            else
            {
                source = workflowClasses
                    .Where(workflowClass =>
                        workflowClass.TenantId == definition.TenantId &&
                        string.Equals(workflowClass.Name, definition.Name, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(workflowClass =>
                    {
                        try
                        {
                            return WorkflowVersion.Parse(workflowClass.Version).RuntimeVersion == definition.Version;
                        }
                        catch
                        {
                            return false;
                        }
                    });
            }

            if (source == null ||
                source.TenantId != definition.TenantId ||
                !string.Equals(source.Name, definition.Name, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            try
            {
                if (WorkflowVersion.Parse(source.Version).RuntimeVersion != definition.Version)
                {
                    skipped++;
                    continue;
                }

                var package = WorkflowClassCompiler.MapToRuntimePackage(source);
                StateMachineDefinition? law = null;
                if (definition.StateMachineDefinitionId.HasValue)
                {
                    law = await _context.StateMachineDefinitions.FirstOrDefaultAsync(
                        item => item.Id == definition.StateMachineDefinitionId.Value,
                        cancellationToken);
                }

                if (law != null &&
                    law.Status == StateMachineStatus.Draft &&
                    WorkflowClassCompiler.HasSameLawGraph(law, package.StateMachineDefinition))
                {
                    law.Publish();
                }

                if (!WorkflowClassCompiler.IsUsablePublishedLaw(law) ||
                    !WorkflowClassCompiler.HasSameLawGraph(law, package.StateMachineDefinition))
                {
                    law = package.StateMachineDefinition;
                    _context.StateMachineDefinitions.Add(law);
                    stateMachinesCreated++;
                }

                var previousSourceId = definition.SourceWorkflowClassId;
                var previousStateMachineId = definition.StateMachineDefinitionId;
                definition.SetClassLineage(source.Id, law!.Id);
                if (previousSourceId != definition.SourceWorkflowClassId ||
                    previousStateMachineId != definition.StateMachineDefinitionId)
                {
                    repaired++;
                }
            }
            catch (Exception ex)
            {
                skipped++;
                _logger.LogWarning(
                    ex,
                    "Could not backfill runtime lineage for workflow definition {DefinitionId} ({Name} v{Version}).",
                    definition.Id,
                    definition.Name,
                    definition.Version);
            }
        }

        if (_context.ChangeTracker.HasChanges())
            await _context.SaveChangesAsync(cancellationToken);

        return new WorkflowDefinitionLineageBackfillResult(
            definitions.Count,
            repaired,
            stateMachinesCreated,
            skipped);
    }
}
