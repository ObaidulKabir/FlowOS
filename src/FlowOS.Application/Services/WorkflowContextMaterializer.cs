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
        await ValidateEventCompatibilityAsync(package.EventDefinitions, binding.TenantId, cancellationToken);

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
        CancellationToken cancellationToken)
    {
        foreach (var candidate in materializedEvents)
        {
            var existing = await _unitOfWork.EventDefinitions
                .GetByEventIdAndTenantAsync(candidate.EventId, tenantId, cancellationToken);
            if (existing == null) continue;

            if (existing.Status != StateMachineStatus.Published ||
                existing.Category != candidate.Category ||
                existing.IsTerminal != candidate.IsTerminal ||
                !EquivalentJson(existing.PayloadSchema, candidate.PayloadSchema))
            {
                throw new InvalidOperationException(
                    $"Context event '{candidate.EventId}' conflicts with an existing tenant event definition.");
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
