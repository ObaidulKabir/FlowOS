using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using System.Text.Json.Nodes;

namespace FlowOS.Application.Services;

public class WorkflowContextMaterializer : IWorkflowContextMaterializer
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowContextBindingValidator _validator;

    public WorkflowContextMaterializer(
        IUnitOfWork unitOfWork,
        IWorkflowContextBindingValidator validator)
    {
        _unitOfWork = unitOfWork;
        _validator = validator;
    }

    public async Task<WorkflowContextCompilationPackage> ActivateAsync(
        WorkflowContextBinding binding,
        WorkflowContextBindingRevision revision,
        CancellationToken cancellationToken = default)
    {
        if (binding.Status == WorkflowContextBindingStatus.Archived)
            throw new InvalidOperationException("Archived context bindings cannot be activated.");
        if (revision.BindingId != binding.Id || binding.DraftRevisionId != revision.Id)
            throw new InvalidOperationException("Only this binding's current draft revision can be activated.");
        if (revision.Status != WorkflowContextBindingRevisionStatus.Draft)
            throw new InvalidOperationException("Only draft context-binding revisions can be activated.");

        var validation = await _validator.ValidateAsync(binding, revision, cancellationToken);
        if (!validation.IsValid)
            throw new WorkflowContextBindingValidationException(validation);

        var source = await _unitOfWork.WorkflowClasses
            .GetByIdAsNoTrackingAsync(revision.SourceWorkflowClassId, cancellationToken)
            ?? throw new KeyNotFoundException("Source workflow template was not found.");

        var package = WorkflowClassCompiler.MapToContextRuntimePackage(source, binding, revision);
        await ValidateEventCompatibilityAsync(
            package.EventDefinitions,
            binding.TenantId,
            source,
            binding.Id,
            revision.Id,
            cancellationToken);

        if (binding.ActiveRevisionId.HasValue)
        {
            var previous = await _unitOfWork.WorkflowContextBindings
                .GetRevisionByIdAsync(binding.ActiveRevisionId.Value, cancellationToken);
            if (previous != null && previous.Status == WorkflowContextBindingRevisionStatus.Active)
            {
                previous.Supersede();

                if (previous.WorkflowDefinitionId.HasValue)
                {
                    var previousWorkflow = await _unitOfWork.WorkflowDefinitions
                        .GetByIdAsync(previous.WorkflowDefinitionId.Value, cancellationToken);
                    previousWorkflow?.Archive();
                }

                if (previous.StateMachineDefinitionId.HasValue)
                {
                    var previousStateMachine = await _unitOfWork.StateMachines
                        .GetByIdAsync(previous.StateMachineDefinitionId.Value, cancellationToken);
                    previousStateMachine?.Archive();
                }
            }
        }

        _unitOfWork.StateMachines.Add(package.StateMachineDefinition);
        _unitOfWork.WorkflowDefinitions.Add(package.WorkflowDefinition);

        foreach (var eventDefinition in package.EventDefinitions)
        {
            var existing = await _unitOfWork.EventDefinitions
                .GetByEventIdAndTenantAsync(eventDefinition.EventId, binding.TenantId, cancellationToken);
            if (existing == null)
            {
                _unitOfWork.EventDefinitions.Add(eventDefinition);
            }
            else
            {
                // DX report F5: a class's own publish_workflowclass/new_version_workflowclass step
                // always registers its declared events with no payload schema (see
                // WorkflowClassCommandHandlers). This binding's eventSourcePayloadSchemas may be the
                // first thing to describe one - fill it in now that ValidateEventCompatibilityAsync has
                // confirmed it isn't a conflicting redefinition. No-ops if existing already has a
                // schema (nothing to enrich) or this binding doesn't declare one either.
                existing.EnrichPayloadSchema(eventDefinition.PayloadSchema);
            }
        }

        // Business-context roles (source.Definition.Roles) are NOT provisioned into FlowOS's own
        // tenant Role/TenantUserRole tables here. They are the modeled application's own roles, not
        // FlowOS IAM roles, and they already rode along as declarative metadata on
        // package.WorkflowDefinition.BusinessRoles (compiled by WorkflowClassCompiler via
        // ContextRoleProvisioningRules). Membership in them is resolved per running instance by
        // IBusinessRoleResolver — nothing to write here, on purpose.

        revision.Activate(
            package.WorkflowDefinition.Id,
            package.StateMachineDefinition.Id,
            package.ContentHash);
        binding.Activate(revision.Id);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return package;
    }

    private async Task ValidateEventCompatibilityAsync(
        IReadOnlyList<EventDefinition> materializedEvents,
        Guid tenantId,
        WorkflowClass source,
        Guid bindingId,
        Guid bindingRevisionId,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in materializedEvents)
        {
            var existing = await _unitOfWork.EventDefinitions
                .GetByEventIdAndTenantAsync(candidate.EventId, tenantId, cancellationToken);
            if (existing == null) continue;

            // DX report F5: an event registered with no payload schema (the default from
            // publish_workflowclass/new_version_workflowclass - see WorkflowClassCommandHandlers)
            // imposed no shape constraint, so a context binding that is the first to declare one via
            // eventSourcePayloadSchemas is enriching that event, not redefining it. Only a genuine
            // disagreement between two *non-blank* schemas - or a mismatched status/category/terminal
            // flag - is a real conflict.
            var schemaConflicts =
                !string.IsNullOrWhiteSpace(existing.PayloadSchema) &&
                !string.IsNullOrWhiteSpace(candidate.PayloadSchema) &&
                !EquivalentJson(existing.PayloadSchema, candidate.PayloadSchema);

            if (existing.Status != StateMachineStatus.Published ||
                existing.Category != candidate.Category ||
                existing.IsTerminal != candidate.IsTerminal ||
                schemaConflicts)
            {
                // DX report F6: name enough to act on, not just the event id - which class/version and
                // which binding revision tried to introduce the conflicting definition. This cannot
                // also name which *earlier* definition it conflicts with (status/category/schema from
                // an unrelated class publish or binding) because EventDefinition does not record its
                // own origin - that would need a separate change to track provenance.
                throw new WorkflowContextEventConflictException(
                    $"Context event '{candidate.EventId}' conflicts with an existing tenant event definition.",
                    candidate.EventId,
                    source.Id,
                    source.Name,
                    source.Version,
                    bindingId,
                    bindingRevisionId);
            }
        }
    }

    private static bool EquivalentJson(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)) return true;
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;

        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
        }
    }
}
