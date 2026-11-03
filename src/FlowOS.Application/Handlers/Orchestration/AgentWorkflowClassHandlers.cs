using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Orchestration;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs.Orchestration;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities.Orchestration;
using FlowOS.Domain.Services;
using FlowOS.Domain.Validation;
using MediatR;

namespace FlowOS.Application.Handlers.Orchestration;

/// <summary>
/// Command and query handlers for <see cref="AgentWorkflowClass"/>.
/// ADR-006 Phase 4A — blueprint CRUD only; dispatch machinery arrives in Phase 4B.
///
/// CR-08: all DateTime values come from the injected <see cref="TimeProvider"/>;
/// no direct clock calls anywhere in this file.
///
/// A Published plan is immutable and dispatchable, so the blueprint is validated on the
/// way in (Create, UpdateDraft) and again at Publish — the last gate before it locks.
/// </summary>
public sealed class AgentWorkflowClassHandlers :
    IRequestHandler<CreateAgentWorkflowClassCommand, AgentWorkflowClassResponseDto>,
    IRequestHandler<UpdateAgentWorkflowClassDraftCommand, AgentWorkflowClassResponseDto>,
    IRequestHandler<PublishAgentWorkflowClassCommand, AgentWorkflowClassResponseDto>,
    IRequestHandler<DeprecateAgentWorkflowClassCommand, AgentWorkflowClassResponseDto>,
    IRequestHandler<AbandonAgentWorkflowClassCommand, AgentWorkflowClassResponseDto>,
    IRequestHandler<DeleteAgentWorkflowClassCommand, Unit>,
    IRequestHandler<ValidateAgentWorkflowBlueprintCommand, ValidationResult>,
    IRequestHandler<GetAgentWorkflowClassQuery, AgentWorkflowClassResponseDto?>,
    IRequestHandler<ListAgentWorkflowClassesQuery, IReadOnlyList<AgentWorkflowClassResponseDto>>,
    IRequestHandler<GetAgentWorkflowClassLineageQuery, IReadOnlyList<AgentWorkflowClassResponseDto>>
{
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _time;
    private readonly IAgentWorkflowBlueprintValidator _validator;

    public AgentWorkflowClassHandlers(
        IUnitOfWork uow,
        TimeProvider time,
        IAgentWorkflowBlueprintValidator validator)
    {
        _uow = uow;
        _time = time;
        _validator = validator;
    }

    // ── Write handlers ─────────────────────────────────────────────────────────

    public async Task<AgentWorkflowClassResponseDto> Handle(
        CreateAgentWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        EnsureBlueprintValid(request.Definition);

        var now = _time.GetUtcNow().UtcDateTime;
        var plan = new AgentWorkflowClass(
            request.TenantId,
            request.Name,
            request.Version,
            request.Definition,
            now,
            request.PreviousVersionId,
            request.ChangeLog);

        await _uow.AgentWorkflowClasses.AddAsync(plan, cancellationToken);
        return Map(plan);
    }

    public async Task<AgentWorkflowClassResponseDto> Handle(
        UpdateAgentWorkflowClassDraftCommand request, CancellationToken cancellationToken)
    {
        EnsureBlueprintValid(request.Definition);

        var plan = await RequireOwnedAsync(request.TenantId, request.Id, cancellationToken);
        plan.UpdateDraft(request.Name, request.Version, request.Definition, request.ChangeLog);
        await _uow.AgentWorkflowClasses.UpdateAsync(plan, cancellationToken);
        return Map(plan);
    }

    public async Task<AgentWorkflowClassResponseDto> Handle(
        PublishAgentWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var plan = await RequireOwnedAsync(request.TenantId, request.Id, cancellationToken);

        // Last gate: publishing locks the blueprint and makes it dispatchable.
        EnsureBlueprintValid(plan.Definition);

        plan.Publish(_time.GetUtcNow().UtcDateTime);
        await _uow.AgentWorkflowClasses.UpdateAsync(plan, cancellationToken);
        return Map(plan);
    }

    public async Task<AgentWorkflowClassResponseDto> Handle(
        DeprecateAgentWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var plan = await RequireOwnedAsync(request.TenantId, request.Id, cancellationToken);
        plan.Deprecate(request.Reason, request.MigrationTargetId);
        await _uow.AgentWorkflowClasses.UpdateAsync(plan, cancellationToken);
        return Map(plan);
    }

    public async Task<AgentWorkflowClassResponseDto> Handle(
        AbandonAgentWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var plan = await RequireOwnedAsync(request.TenantId, request.Id, cancellationToken);
        plan.Abandon();
        await _uow.AgentWorkflowClasses.UpdateAsync(plan, cancellationToken);
        return Map(plan);
    }

    public async Task<Unit> Handle(
        DeleteAgentWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var plan = await RequireOwnedAsync(request.TenantId, request.Id, cancellationToken);
        var hasExecutions = await _uow.AgentWorkflowClasses.HasExecutionsAsync(
            request.TenantId, request.Id, cancellationToken);

        plan.Delete(hasExecutions);  // throws GOV-DEL-001 / GOV-DEL-002 on guard failure
        await _uow.AgentWorkflowClasses.DeleteAsync(request.TenantId, request.Id, cancellationToken);
        return Unit.Value;
    }

    /// <summary>
    /// Validate a blueprint without storing it. Returns the result instead of throwing,
    /// so the dashboard can show every problem at once before the author publishes.
    /// </summary>
    public Task<ValidationResult> Handle(
        ValidateAgentWorkflowBlueprintCommand request, CancellationToken cancellationToken) =>
        Task.FromResult(_validator.Validate(request.Definition));

    // ── Query handlers ─────────────────────────────────────────────────────────

    public async Task<AgentWorkflowClassResponseDto?> Handle(
        GetAgentWorkflowClassQuery request, CancellationToken cancellationToken)
    {
        var plan = await _uow.AgentWorkflowClasses.GetByIdAsync(
            request.TenantId, request.Id, cancellationToken);
        return plan is null ? null : Map(plan);
    }

    public async Task<IReadOnlyList<AgentWorkflowClassResponseDto>> Handle(
        ListAgentWorkflowClassesQuery request, CancellationToken cancellationToken)
    {
        var plans = await _uow.AgentWorkflowClasses.ListAsync(
            request.TenantId, request.Status, request.Skip, request.Take, cancellationToken);
        return plans.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<AgentWorkflowClassResponseDto>> Handle(
        GetAgentWorkflowClassLineageQuery request, CancellationToken cancellationToken)
    {
        var chain = await _uow.AgentWorkflowClasses.GetLineageAsync(
            request.TenantId, request.Id, request.MaxDepth, cancellationToken);
        return chain.Select(Map).ToList();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void EnsureBlueprintValid(AgentWorkflowBlueprint? definition)
    {
        var result = _validator.Validate(definition);
        if (!result.IsValid)
            throw new AgentWorkflowClassValidationException(result);
    }

    private async Task<AgentWorkflowClass> RequireOwnedAsync(
        Guid tenantId, Guid id, CancellationToken ct)
    {
        var plan = await _uow.AgentWorkflowClasses.GetByIdAsync(tenantId, id, ct);
        if (plan is null)
            throw new KeyNotFoundException($"AgentWorkflowClass {id} not found.");
        return plan;
    }

    private static AgentWorkflowClassResponseDto Map(AgentWorkflowClass p) =>
        new()
        {
            Id = p.Id,
            TenantId = p.TenantId,
            Name = p.Name,
            Version = p.Version,
            Status = p.Status,
            Scope = p.Scope,
            CreatedAt = p.CreatedAt,
            PublishedAt = p.PublishedAt,
            PreviousVersionId = p.PreviousVersionId,
            ChangeLog = p.ChangeLog,
            DeprecationReason = p.DeprecationReason,
            DeprecationMigrationTargetId = p.DeprecationMigrationTargetId,
            FlowOsVersion = p.FlowOsVersion,
            Definition = p.Definition,
        };
}

/// <summary>
/// Thrown when an <see cref="AgentWorkflowBlueprint"/> fails structural validation.
/// Mirrors <c>WorkflowClassValidationException</c>; carries every error so the caller
/// can show them all rather than one at a time.
/// </summary>
public sealed class AgentWorkflowClassValidationException : Exception
{
    public ValidationResult ValidationResult { get; }

    public AgentWorkflowClassValidationException(ValidationResult validationResult)
        : base(BuildMessage(validationResult))
    {
        ValidationResult = validationResult;
    }

    private static string BuildMessage(ValidationResult result)
    {
        var detail = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}"));
        return $"AgentWorkflowClass blueprint is invalid. {detail}";
    }
}
