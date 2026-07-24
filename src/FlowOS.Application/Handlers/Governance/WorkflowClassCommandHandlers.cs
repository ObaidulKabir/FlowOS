using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Governance;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Application.Services;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Domain.Validation;
using FlowOS.Domain.ValueObjects;
using MediatR;

namespace FlowOS.Application.Handlers.Governance;

public class WorkflowClassCommandHandlers :
    IRequestHandler<CreateWorkflowClassCommand, WorkflowClassResponseDto>,
    IRequestHandler<UpdateWorkflowClassCommand, WorkflowClassResponseDto>,
    IRequestHandler<PublishWorkflowClassCommand, WorkflowClassResponseDto>,
    IRequestHandler<SubmitWorkflowClassCommand, WorkflowClassResponseDto>,
    IRequestHandler<WithdrawWorkflowClassCommand, WorkflowClassResponseDto>,
    IRequestHandler<ValidateWorkflowClassCommand, ValidationResult>,
    IRequestHandler<DeprecateWorkflowClassCommand, WorkflowClassResponseDto>,
    IRequestHandler<DeleteWorkflowClassCommand, Unit>,
    IRequestHandler<AbandonWorkflowClassCommand, WorkflowClassResponseDto>,
    IRequestHandler<ApproveWorkflowClassCommand, WorkflowClassResponseDto>,
    IRequestHandler<CopyWorkflowClassCommand, WorkflowClassResponseDto>,
    IRequestHandler<CreateNewWorkflowClassVersionCommand, WorkflowClassResponseDto>,
    IRequestHandler<LintWorkflowClassCommand, IReadOnlyList<LintError>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowClassManager _manager;
    private readonly IWorkflowClassVersionManager _versionManager;
    private readonly IWorkflowJsonLinter _linter;

    public WorkflowClassCommandHandlers(
        IUnitOfWork unitOfWork,
        IWorkflowClassManager manager,
        IWorkflowClassVersionManager versionManager,
        IWorkflowJsonLinter linter)
    {
        _unitOfWork = unitOfWork;
        _manager = manager;
        _versionManager = versionManager;
        _linter = linter;
    }

    public async Task<WorkflowClassResponseDto> Handle(CreateWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var workflowClass = new WorkflowClass(request.TenantId, request.Name, request.Version, request.Definition);
        var validationResult = _manager.CreateDraft(workflowClass);
        if (!validationResult.IsValid)
            throw new WorkflowClassValidationException(validationResult);

        _unitOfWork.WorkflowClasses.Add(workflowClass);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(workflowClass);
    }

    public async Task<WorkflowClassResponseDto> Handle(UpdateWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var wc = await GetOwnedAsync(request.Id, request.TenantId, cancellationToken);
        wc.UpdateDraft(request.Name, request.Version, request.Definition);

        var result = _manager.ValidateOnly(wc);
        if (!result.IsValid)
            throw new WorkflowClassValidationException(result);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(wc);
    }

    public async Task<WorkflowClassResponseDto> Handle(PublishWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var wc = await GetOwnedAsync(request.Id, request.TenantId, cancellationToken);
        var result = _manager.Publish(wc);
        if (!result.IsValid)
            throw new WorkflowClassValidationException(result);

        var definition = WorkflowClassCompiler.MapToRuntimeDefinition(wc);
        var existing = await _unitOfWork.WorkflowDefinitions
            .GetByNameAndVersionAsync(definition.Name, definition.Version, definition.TenantId, cancellationToken);

        if (existing == null)
            _unitOfWork.WorkflowDefinitions.Add(definition);

        if (wc.Definition?.Events != null)
        {
            foreach (var evtBp in wc.Definition.Events)
            {
                var existingEvent = await _unitOfWork.EventDefinitions
                    .GetByEventIdAndTenantAsync(evtBp.EventId, wc.TenantId, cancellationToken);

                if (existingEvent == null)
                {
                    var entityType = !string.IsNullOrEmpty(wc.Definition.StateMachine?.EntityType)
                        ? wc.Definition.StateMachine.EntityType
                        : "Workflow";

                    var newEvent = new EventDefinition(
                        evtBp.EventId,
                        wc.TenantId,
                        !string.IsNullOrEmpty(evtBp.Name) ? evtBp.Name : evtBp.EventId,
                        !string.IsNullOrEmpty(evtBp.Description) ? evtBp.Description : $"Event {evtBp.EventId}",
                        entityType,
                        evtBp.Category,
                        1,
                        null,
                        evtBp.IsTerminal
                    );
                    newEvent.Publish();
                    _unitOfWork.EventDefinitions.Add(newEvent);
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(wc);
    }

    public async Task<WorkflowClassResponseDto> Handle(SubmitWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var wc = await GetOwnedAsync(request.Id, request.TenantId, cancellationToken);
        EnsureValid(_manager.SubmitForReview(wc));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(wc);
    }

    public async Task<WorkflowClassResponseDto> Handle(WithdrawWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var wc = await GetOwnedAsync(request.Id, request.TenantId, cancellationToken);
        EnsureValid(_manager.WithdrawSubmission(wc));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(wc);
    }

    public async Task<ValidationResult> Handle(ValidateWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var wc = await GetOwnedAsync(request.Id, request.TenantId, cancellationToken);
        return _manager.ValidateOnly(wc);
    }

    public async Task<WorkflowClassResponseDto> Handle(DeprecateWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var wc = await GetOwnedAsync(request.Id, request.TenantId, cancellationToken);
        EnsureValid(_manager.Deprecate(wc));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(wc);
    }

    public async Task<Unit> Handle(DeleteWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var wc = await GetOwnedAsync(request.Id, request.TenantId, cancellationToken);
        var hasInstances = await _unitOfWork.WorkflowInstances.AnyForWorkflowClassAsync(request.Id, cancellationToken);
        wc.Delete(hasInstances);
        _unitOfWork.WorkflowClasses.Remove(wc);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<WorkflowClassResponseDto> Handle(AbandonWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var wc = await GetOwnedAsync(request.Id, request.TenantId, cancellationToken);
        wc.Abandon(request.TenantId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(wc);
    }

    public async Task<WorkflowClassResponseDto> Handle(ApproveWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var wc = await _unitOfWork.WorkflowClasses.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"WorkflowClass {request.Id} not found.");

        EnsureValid(_manager.ApproveAsPublic(wc));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(wc);
    }

    public async Task<WorkflowClassResponseDto> Handle(CopyWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        var wc = await _unitOfWork.WorkflowClasses.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"WorkflowClass {request.Id} not found.");

        if (wc.Scope != WorkflowClassScope.Public)
            throw new InvalidOperationException("Only Public WorkflowClasses can be copied.");

        if (request.NewTenantId != request.TenantId)
            throw new UnauthorizedAccessException("Cannot copy to a different tenant.");

        var copy = _versionManager.CreateCopyForTenant(wc, request.NewTenantId);
        _unitOfWork.WorkflowClasses.Add(copy);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(copy);
    }

    public async Task<WorkflowClassResponseDto> Handle(CreateNewWorkflowClassVersionCommand request, CancellationToken cancellationToken)
    {
        var wc = await GetOwnedAsync(request.Id, request.TenantId, cancellationToken);
        var current = WorkflowVersion.Parse(wc.Version);
        var newVersion = _versionManager.CreateNewVersion(wc, current.BumpMinor().ToString());
        _unitOfWork.WorkflowClasses.Add(newVersion);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(newVersion);
    }

    public Task<IReadOnlyList<LintError>> Handle(LintWorkflowClassCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.JsonContent))
            throw new ArgumentException("JsonContent is required.");

        return Task.FromResult<IReadOnlyList<LintError>>(_linter.Lint(request.JsonContent).ToList());
    }

    private async Task<WorkflowClass> GetOwnedAsync(Guid id, Guid tenantId, CancellationToken cancellationToken)
    {
        var wc = await _unitOfWork.WorkflowClasses.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"WorkflowClass {id} not found.");

        if (wc.TenantId != tenantId)
            throw new UnauthorizedAccessException("WorkflowClass is not owned by the current tenant.");

        return wc;
    }

    private static void EnsureValid(ValidationResult result)
    {
        if (!result.IsValid)
            throw new WorkflowClassValidationException(result);
    }

    internal static WorkflowClassResponseDto MapToDto(WorkflowClass wc) => new()
    {
        Id = wc.Id,
        TenantId = wc.TenantId,
        Name = wc.Name,
        Version = wc.Version,
        Scope = wc.Scope,
        Status = wc.Status,
        CreatedAt = wc.CreatedAt,
        PublishedAt = wc.PublishedAt,
        PreviousVersionId = wc.PreviousVersionId,
        Definition = wc.Definition
    };
}

public class WorkflowClassValidationException : Exception
{
    public ValidationResult ValidationResult { get; }

    public WorkflowClassValidationException(ValidationResult validationResult)
        : base("WorkflowClass validation failed.")
    {
        ValidationResult = validationResult;
    }
}
