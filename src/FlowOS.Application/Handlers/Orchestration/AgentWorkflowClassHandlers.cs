using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Orchestration;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs.Orchestration;
using FlowOS.Domain.Entities.Orchestration;
using MediatR;

namespace FlowOS.Application.Handlers.Orchestration;

/// <summary>
/// Command and query handlers for <see cref="AgentWorkflowClass"/>.
/// ADR-006 Phase 4A — blueprint CRUD only; dispatch machinery arrives in Phase 4B.
///
/// CR-08: all DateTime values come from the injected <see cref="TimeProvider"/>;
/// no direct <c>DateTime.UtcNow</c> calls.
/// </summary>
public sealed class AgentWorkflowClassHandlers :
    IRequestHandler<CreateAgentWorkflowClassCommand, AgentWorkflowClassResponseDto>,
    IRequestHandler<UpdateAgentWorkflowClassDraftCommand, AgentWorkflowClassResponseDto>,
    IRequestHandler<PublishAgentWorkflowClassCommand, AgentWorkflowClassResponseDto>,
    IRequestHandler<DeprecateAgentWorkflowClassCommand, AgentWorkflowClassResponseDto>,
    IRequestHandler<AbandonAgentWorkflowClassCommand, AgentWorkflowClassResponseDto>,
    IRequestHandler<DeleteAgentWorkflowClassCommand, Unit>,
    IRequestHandler<GetAgentWorkflowClassQuery, AgentWorkflowClassResponseDto?>,
    IRequestHandler<ListAgentWorkflowClassesQuery, IReadOnlyList<AgentWorkflowClassResponseDto>>,
    IRequestHandler<GetAgentWorkflowClassLineageQuery, IReadOnlyList<AgentWorkflowClassResponseDto>>
{
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _time;

    public AgentWorkflowClassHandlers(IUnitOfWork uow, TimeProvider time)
    {
        _uow = uow;
        _time = time;
    }

    // ── Write handlers ─────────────────────────────────────────────────────────

    public async Task<AgentWorkflowClassResponseDto> Handle(
        CreateAgentWorkflowClassCommand request, CancellationToken cancellationToken)
    {
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
        var plan = await RequireOwnedAsync(request.TenantId, request.Id, cancellationToken);
        plan.UpdateDraft(request.Name, request.Version, request.Definition, request.ChangeLog);
        await _uow.AgentWorkflowClasses.UpdateAsync(plan, cancellationToken);
        return Map(plan);
    }

    public async Task<AgentWorkflowClassResponseDto> Handle(
        PublishAgentWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var plan = await RequireOwnedAsync(request.TenantId, request.Id, cancellationToken);
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
