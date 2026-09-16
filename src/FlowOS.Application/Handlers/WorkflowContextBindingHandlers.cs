using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using MediatR;

namespace FlowOS.Application.Handlers;

public sealed class WorkflowContextBindingHandlers :
    IRequestHandler<CreateWorkflowContextBindingCommand, WorkflowContextBindingDto>,
    IRequestHandler<UpdateWorkflowContextBindingCommand, WorkflowContextBindingDto>,
    IRequestHandler<ValidateWorkflowContextBindingCommand, WorkflowContextBindingValidationDto>,
    IRequestHandler<ActivateWorkflowContextBindingCommand, WorkflowContextBindingDto>,
    IRequestHandler<ArchiveWorkflowContextBindingCommand, WorkflowContextBindingDto>,
    IRequestHandler<ListWorkflowContextBindingsQuery, IReadOnlyList<WorkflowContextBindingDto>>,
    IRequestHandler<GetWorkflowContextBindingQuery, WorkflowContextBindingDto?>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowContextBindingValidator _validator;
    private readonly IWorkflowContextMaterializer _materializer;

    public WorkflowContextBindingHandlers(
        IUnitOfWork unitOfWork,
        IWorkflowContextBindingValidator validator,
        IWorkflowContextMaterializer materializer)
    {
        _unitOfWork = unitOfWork;
        _validator = validator;
        _materializer = materializer;
    }

    public async Task<WorkflowContextBindingDto> Handle(
        CreateWorkflowContextBindingCommand request,
        CancellationToken cancellationToken)
    {
        if (await _unitOfWork.WorkflowContextBindings.GetByContextTypeAsync(
                request.ContextType, request.TenantId, cancellationToken) != null)
        {
            throw new InvalidOperationException($"Context type '{request.ContextType}' already has a binding in this tenant.");
        }
        if (await _unitOfWork.WorkflowContextBindings.GetByNameAsync(
                request.Name, request.TenantId, cancellationToken) != null)
        {
            throw new InvalidOperationException($"Context binding name '{request.Name}' already exists in this tenant.");
        }

        var source = await ResolveSourceAsync(
            request.SourceWorkflowClassId,
            request.TenantId,
            cancellationToken);
        var binding = new WorkflowContextBinding(request.TenantId, request.ContextType, request.Name);
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            request.Definition);
        binding.SetDraftRevision(revision.Id);

        _unitOfWork.WorkflowContextBindings.Add(binding);
        _unitOfWork.WorkflowContextBindings.AddRevision(revision);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(binding, null, revision);
    }

    public async Task<WorkflowContextBindingDto> Handle(
        UpdateWorkflowContextBindingCommand request,
        CancellationToken cancellationToken)
    {
        var binding = await GetBindingAsync(request.BindingId, request.TenantId, cancellationToken);
        if (binding.Status == WorkflowContextBindingStatus.Archived)
            throw new InvalidOperationException("Archived context bindings cannot be updated.");

        WorkflowContextBindingRevision revision;
        if (binding.DraftRevisionId.HasValue)
        {
            revision = await _unitOfWork.WorkflowContextBindings
                .GetRevisionByIdAsync(binding.DraftRevisionId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Draft context-binding revision was not found.");
        }
        else
        {
            var nextRevision = await _unitOfWork.WorkflowContextBindings
                .GetNextRevisionNumberAsync(binding.Id, cancellationToken);
            var sourceId = request.SourceWorkflowClassId;
            if (!sourceId.HasValue && binding.ActiveRevisionId.HasValue)
            {
                var active = await _unitOfWork.WorkflowContextBindings
                    .GetRevisionByIdAsNoTrackingAsync(binding.ActiveRevisionId.Value, cancellationToken);
                sourceId = active?.SourceWorkflowClassId;
            }
            if (!sourceId.HasValue)
                throw new ArgumentException("SourceWorkflowClassId is required.");

            var source = await ResolveSourceAsync(sourceId.Value, request.TenantId, cancellationToken);
            revision = new WorkflowContextBindingRevision(
                binding.Id,
                nextRevision,
                source.Id,
                source.Version,
                request.Definition);
            _unitOfWork.WorkflowContextBindings.AddRevision(revision);
            binding.SetDraftRevision(revision.Id);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var activeDto = await GetRevisionAsync(binding.ActiveRevisionId, cancellationToken);
            return Map(binding, activeDto, revision);
        }

        var updatedSourceId = request.SourceWorkflowClassId ?? revision.SourceWorkflowClassId;
        var updatedSource = await ResolveSourceAsync(updatedSourceId, request.TenantId, cancellationToken);
        revision.UpdateDraft(updatedSource.Id, updatedSource.Version, request.Definition);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var activeRevision = await GetRevisionAsync(binding.ActiveRevisionId, cancellationToken);
        return Map(binding, activeRevision, revision);
    }

    public async Task<WorkflowContextBindingValidationDto> Handle(
        ValidateWorkflowContextBindingCommand request,
        CancellationToken cancellationToken)
    {
        var binding = await GetBindingAsync(request.BindingId, request.TenantId, cancellationToken);
        var revisionId = binding.DraftRevisionId ?? binding.ActiveRevisionId
            ?? throw new InvalidOperationException("Context binding has no revision to validate.");
        var revision = await _unitOfWork.WorkflowContextBindings
            .GetRevisionByIdAsNoTrackingAsync(revisionId, cancellationToken)
            ?? throw new InvalidOperationException("Context-binding revision was not found.");

        var validation = await _validator.ValidateAsync(binding, revision, cancellationToken);
        return new WorkflowContextBindingValidationDto(validation.IsValid, validation.Errors);
    }

    public async Task<WorkflowContextBindingDto> Handle(
        ActivateWorkflowContextBindingCommand request,
        CancellationToken cancellationToken)
    {
        var binding = await GetBindingAsync(request.BindingId, request.TenantId, cancellationToken);
        if (!binding.DraftRevisionId.HasValue)
            throw new InvalidOperationException("Context binding has no draft revision to activate.");

        var revision = await _unitOfWork.WorkflowContextBindings
            .GetRevisionByIdAsync(binding.DraftRevisionId.Value, cancellationToken)
            ?? throw new InvalidOperationException("Draft context-binding revision was not found.");

        await _materializer.ActivateAsync(binding, revision, cancellationToken);
        return Map(binding, revision, null);
    }

    public async Task<WorkflowContextBindingDto> Handle(
        ArchiveWorkflowContextBindingCommand request,
        CancellationToken cancellationToken)
    {
        var binding = await GetBindingAsync(request.BindingId, request.TenantId, cancellationToken);
        binding.Archive();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var active = await GetRevisionAsync(binding.ActiveRevisionId, cancellationToken);
        return Map(binding, active, null);
    }

    public async Task<IReadOnlyList<WorkflowContextBindingDto>> Handle(
        ListWorkflowContextBindingsQuery request,
        CancellationToken cancellationToken)
    {
        var bindings = await _unitOfWork.WorkflowContextBindings
            .ListAsync(request.TenantId, cancellationToken);
        var revisions = await _unitOfWork.WorkflowContextBindings
            .ListRevisionsAsync(bindings.Select(x => x.Id), cancellationToken);

        if (request.SourceWorkflowClassId.HasValue)
        {
            var matchingBindingIds = revisions
                .Where(x => x.SourceWorkflowClassId == request.SourceWorkflowClassId.Value)
                .Select(x => x.BindingId)
                .ToHashSet();
            bindings = bindings.Where(x => matchingBindingIds.Contains(x.Id)).ToList();
        }

        var byId = revisions.ToDictionary(x => x.Id);
        return bindings
            .Select(binding => Map(
                binding,
                GetRevision(binding.ActiveRevisionId, byId),
                GetRevision(binding.DraftRevisionId, byId)))
            .ToList();
    }

    public async Task<WorkflowContextBindingDto?> Handle(
        GetWorkflowContextBindingQuery request,
        CancellationToken cancellationToken)
    {
        var binding = await _unitOfWork.WorkflowContextBindings
            .GetByIdAsNoTrackingAsync(request.BindingId, request.TenantId, cancellationToken);
        if (binding == null) return null;

        var revisions = await _unitOfWork.WorkflowContextBindings
            .ListRevisionsAsync(new[] { binding.Id }, cancellationToken);
        var byId = revisions.ToDictionary(x => x.Id);
        return Map(
            binding,
            GetRevision(binding.ActiveRevisionId, byId),
            GetRevision(binding.DraftRevisionId, byId));
    }

    private async Task<WorkflowClass> ResolveSourceAsync(
        Guid sourceWorkflowClassId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var source = await _unitOfWork.WorkflowClasses
            .GetByIdAsNoTrackingAsync(sourceWorkflowClassId, cancellationToken);
        if (source == null ||
            (source.TenantId != tenantId && source.Scope != WorkflowClassScope.Public))
        {
            throw new KeyNotFoundException("Source workflow template was not found.");
        }
        if (source.Status != WorkflowClassStatus.Published &&
            source.Status != WorkflowClassStatus.Public &&
            source.Status != WorkflowClassStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Source workflow template cannot be used while it is {source.Status}.");
        }
        return source;
    }

    private async Task<WorkflowContextBinding> GetBindingAsync(
        Guid bindingId,
        Guid tenantId,
        CancellationToken cancellationToken)
        => await _unitOfWork.WorkflowContextBindings.GetByIdAsync(bindingId, tenantId, cancellationToken)
           ?? throw new KeyNotFoundException("Workflow context binding was not found.");

    private async Task<WorkflowContextBindingRevision?> GetRevisionAsync(
        Guid? revisionId,
        CancellationToken cancellationToken)
        => revisionId.HasValue
            ? await _unitOfWork.WorkflowContextBindings
                .GetRevisionByIdAsNoTrackingAsync(revisionId.Value, cancellationToken)
            : null;

    private static WorkflowContextBindingRevision? GetRevision(
        Guid? revisionId,
        IReadOnlyDictionary<Guid, WorkflowContextBindingRevision> revisions)
        => revisionId.HasValue && revisions.TryGetValue(revisionId.Value, out var revision)
            ? revision
            : null;

    private static WorkflowContextBindingDto Map(
        WorkflowContextBinding binding,
        WorkflowContextBindingRevision? activeRevision,
        WorkflowContextBindingRevision? draftRevision)
        => new(
            binding.Id,
            binding.TenantId,
            binding.ContextType,
            binding.Name,
            binding.Status.ToString(),
            binding.ActiveRevisionId,
            binding.DraftRevisionId,
            activeRevision == null ? null : MapRevision(activeRevision),
            draftRevision == null ? null : MapRevision(draftRevision),
            binding.CreatedAtUtc,
            binding.UpdatedAtUtc,
            binding.ArchivedAtUtc);

    private static WorkflowContextBindingRevisionDto MapRevision(WorkflowContextBindingRevision revision)
        => new(
            revision.Id,
            revision.Revision,
            revision.SourceWorkflowClassId,
            revision.SourceWorkflowClassVersion,
            revision.Status.ToString(),
            revision.Definition,
            revision.WorkflowDefinitionId,
            revision.StateMachineDefinitionId,
            revision.ContentHash,
            revision.CreatedAtUtc,
            revision.ActivatedAtUtc,
            revision.SupersededAtUtc);
}
