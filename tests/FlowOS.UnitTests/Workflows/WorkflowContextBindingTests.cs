using System.Text.Json;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.DTOs;
using FlowOS.Application.Handlers;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Domain.ValueObjects;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowContextBindingTests
{
    [Fact]
    public void Binding_NormalizesIdentity_AndArchivesWithoutDroppingActiveRevision()
    {
        var binding = new WorkflowContextBinding(Guid.NewGuid(), " Expense ", " Expense Approval ");
        var revisionId = Guid.NewGuid();

        binding.SetDraftRevision(revisionId);
        binding.Activate(revisionId);
        binding.Archive();

        Assert.Equal("EXPENSE", binding.NormalizedContextType);
        Assert.Equal("EXPENSE APPROVAL", binding.NormalizedName);
        Assert.Equal(WorkflowContextBindingStatus.Archived, binding.Status);
        Assert.Equal(revisionId, binding.ActiveRevisionId);
        Assert.Null(binding.DraftRevisionId);
    }

    [Fact]
    public void ActivatedRevision_IsImmutable_AndCanBeSuperseded()
    {
        var revision = CreateRevision(new WorkflowContextBinding(Guid.NewGuid(), "Expense", "ExpenseApproval"));

        revision.Activate(Guid.NewGuid(), Guid.NewGuid(), "ABC");
        Assert.Throws<InvalidOperationException>(() => revision.UpdateDraft(
            Guid.NewGuid(), "2.0.0", new WorkflowContextBindingDefinition { EntityType = "Other" }));

        revision.Supersede();
        Assert.Equal(WorkflowContextBindingRevisionStatus.Superseded, revision.Status);
        Assert.NotNull(revision.SupersededAtUtc);
    }

    [Fact]
    public void Snapshot_MergesCanonicalDelta_AndIncrementsConcurrencyVersion()
    {
        var initial = new Dictionary<string, JsonElement>
        {
            ["Amount"] = JsonSerializer.SerializeToElement(100)
        };
        var snapshot = new WorkflowContextSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), initial);

        snapshot.Merge(new Dictionary<string, JsonElement>
        {
            ["Description"] = JsonSerializer.SerializeToElement("Travel")
        });

        Assert.Equal(2, snapshot.ConcurrencyVersion);
        Assert.Equal(100, snapshot.CanonicalData["Amount"].GetInt32());
        Assert.Equal("Travel", snapshot.CanonicalData["Description"].GetString());
    }

    [Fact]
    public void ContextCompiler_AppliesAliasesRolesConstraintsSlaAndActions()
    {
        var source = CreateSource();
        var binding = new WorkflowContextBinding(Guid.NewGuid(), "Expense", "ExpenseApproval");
        var revision = CreateRevision(binding, sourceId: source.Id);

        var package = WorkflowClassCompiler.MapToContextRuntimePackage(source, binding, revision);
        var review = package.WorkflowDefinition.Steps.Single(x => x.StepId == "Review");
        var transition = package.StateMachineDefinition.Transitions.Single();

        Assert.Equal(binding.TenantId, package.WorkflowDefinition.TenantId);
        Assert.Equal("ExpenseApproval", package.WorkflowDefinition.Name);
        Assert.Equal("ExpenseEntity", package.StateMachineDefinition.EntityType);
        Assert.Equal("EVT-EXP-APPROVE", transition.EventId);
        Assert.Equal("Amount <= ApprovalLimit", transition.Constraints["Expression"]);
        Assert.Equal("FinanceManager", transition.Constraints["Role"]);
        Assert.Contains("FinanceManager", review.AllowedRoles);
        Assert.Equal("EVT-EXP-APPROVE", review.Sla!.TimeoutEvent);
        Assert.Equal("FinanceManager", review.Sla.EscalationRole);
        Assert.Equal("EVT-EXP-APPROVE", review.OnEntry.Single().Target);
        Assert.Equal("event.publish.EVT-EXP-APPROVE", review.OnEntry.Single().Capability);
        Assert.Contains(package.EventDefinitions, x => x.EventId == "EVT-EXP-APPROVE");
    }

    [Fact]
    public void ContextCompiler_DoesNotMutateSourceTemplate()
    {
        var source = CreateSource();
        var originalEvent = source.Definition.StateMachine.Transitions.Single().EventId;
        var originalRole = source.Definition.Workflow.Steps.Single(x => x.StepId == "Review").RequiredRoles.Single();
        var binding = new WorkflowContextBinding(Guid.NewGuid(), "Expense", "ExpenseApproval");

        _ = WorkflowClassCompiler.MapToContextRuntimePackage(source, binding, CreateRevision(binding, sourceId: source.Id));

        Assert.Equal("EVT-APPROVE", originalEvent);
        Assert.Equal("Approver", originalRole);
        Assert.Equal("EVT-APPROVE", source.Definition.StateMachine.Transitions.Single().EventId);
        Assert.Equal("Approver", source.Definition.Workflow.Steps.Single(x => x.StepId == "Review").RequiredRoles.Single());
    }

    [Fact]
    public void DifferentBindingRevisions_MaterializeIndependentRuntimePackages()
    {
        var source = CreateSource();
        var binding = new WorkflowContextBinding(Guid.NewGuid(), "Expense", "ExpenseApproval");
        var revisionOne = CreateRevision(binding, 1, source.Id);
        var revisionTwo = CreateRevision(binding, 2, source.Id);

        var first = WorkflowClassCompiler.MapToContextRuntimePackage(source, binding, revisionOne);
        var second = WorkflowClassCompiler.MapToContextRuntimePackage(source, binding, revisionTwo);

        Assert.NotEqual(first.WorkflowDefinition.Id, second.WorkflowDefinition.Id);
        Assert.NotEqual(first.StateMachineDefinition.Id, second.StateMachineDefinition.Id);
        Assert.Equal(1, first.WorkflowDefinition.Version);
        Assert.Equal(2, second.WorkflowDefinition.Version);
        Assert.Equal(revisionOne.Id, first.WorkflowDefinition.ContextBindingRevisionId);
        Assert.Equal(revisionTwo.Id, second.WorkflowDefinition.ContextBindingRevisionId);
    }

    [Fact]
    public void LegacyCompiler_RemainsUnbound()
    {
        var source = CreateSource();
        var definition = WorkflowClassCompiler.MapToRuntimeDefinition(source);

        Assert.Null(definition.ContextBindingRevisionId);
        Assert.Null(definition.StateMachineDefinitionId);
        Assert.Equal(source.Name, definition.Name);
    }

    [Fact]
    public async Task Validator_ReturnsStableCodes_ForInvalidBindingConfiguration()
    {
        var source = CreateSource();
        source.Definition.Workflow.Steps.Single().OnEntry.Clear();
        source.UpdateDraft(
            source.Name,
            source.Version,
            source.Definition with
            {
                ContextSchema = """{"type":"object","required":["Amount","ApprovalLimit"],"properties":{"Amount":{"type":"number"},"ApprovalLimit":{"type":"number"}}}"""
            });
        var publish = new WorkflowClassManager().Publish(source);
        Assert.True(
            publish.IsValid,
            string.Join("; ", publish.Errors.Select(x => $"{x.Code}: {x.Message}")));

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        context.WorkflowClasses.Add(source);
        await context.SaveChangesAsync();

        var binding = new WorkflowContextBinding(source.TenantId, "Expense", "ExpenseApproval");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition
            {
                EntityType = string.Empty,
                EventAliases = new Dictionary<string, string>
                {
                    ["EVT-UNKNOWN"] = "EVT-EXP-UNKNOWN"
                },
                InputMapping = new Dictionary<string, string>
                {
                    ["_Reserved"] = "invalid path"
                },
                EventInputMappings = new Dictionary<string, Dictionary<string, string>>
                {
                    ["EVT-APPROVE"] = new() { ["ApprovalLimit"] = "payload.limit" }
                },
                ConditionParameters = new Dictionary<string, JsonElement>
                {
                    ["ApprovalLimit"] = JsonSerializer.SerializeToElement(1000),
                    ["root"] = default
                },
                DecisionProviderOverrides = new Dictionary<string, string>
                {
                    ["UnknownProvider"] = "missing-provider"
                },
                SourcePayloadSchema = "{"
            });

        var validator = new WorkflowContextBindingValidator(
            new UnitOfWork(context),
            new Mock<IPolicyDecisionPluginRegistry>().Object);
        var result = await validator.ValidateAsync(binding, revision);

        var codes = result.Errors.Select(x => x.Code).ToHashSet();
        Assert.Contains("CTX-ENTITY-001", codes);
        Assert.Contains("CTX-EVT-001", codes);
        Assert.Contains("CTX-MAP-001", codes);
        Assert.Contains("CTX-MAP-002", codes);
        Assert.Contains("CTX-PARAM-002", codes);
        Assert.Contains("CTX-PARAM-003", codes);
        Assert.Contains("CTX-PLUGIN-001", codes);
        Assert.Contains("CTX-PLUGIN-002", codes);
        Assert.Contains("CTX-SCHEMA-002", codes);
        Assert.Contains("CTX-SCHEMA-005", codes);
        Assert.Contains("CTX-SCHEMA-006", codes);
    }

    [Fact]
    public async Task Validator_HidesForeignPrivateSource_ButAllowsItAfterPublicApproval()
    {
        var source = CreateSource();
        source.Definition.Workflow.Steps.Single().OnEntry.Clear();
        var manager = new WorkflowClassManager();
        Assert.True(manager.Publish(source).IsValid);

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        context.WorkflowClasses.Add(source);
        await context.SaveChangesAsync();

        var binding = new WorkflowContextBinding(Guid.NewGuid(), "Expense", "ExpenseApproval");
        context.Roles.Add(new FlowOS.Security.Models.Role(binding.TenantId, "Approver"));
        await context.SaveChangesAsync();
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition { EntityType = "ExpenseEntity" });
        var validator = new WorkflowContextBindingValidator(
            new UnitOfWork(context),
            new Mock<IPolicyDecisionPluginRegistry>().Object);

        var privateResult = await validator.ValidateAsync(binding, revision);
        Assert.Contains(privateResult.Errors, x => x.Code == "CTX-SOURCE-001");

        Assert.True(manager.SubmitForReview(source).IsValid);
        Assert.True(manager.ApproveAsPublic(source).IsValid);
        await context.SaveChangesAsync();

        var publicResult = await validator.ValidateAsync(binding, revision);
        Assert.True(
            publicResult.IsValid,
            string.Join("; ", publicResult.Errors.Select(x => $"{x.Code}: {x.Message}")));
    }

    [Fact]
    public async Task ExecutionContext_PayloadlessEvent_RetainsSnapshotWithoutRevalidatingSourceSchema()
    {
        var source = CreateSource();
        var binding = new WorkflowContextBinding(source.TenantId, "Expense", "ExpenseApproval");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition
            {
                EntityType = "ExpenseEntity",
                InputMapping = new Dictionary<string, string> { ["Amount"] = "amount" },
                SourcePayloadSchema = """{"type":"object","required":["amount"]}""",
                ConditionParameters = new Dictionary<string, JsonElement>
                {
                    ["ApprovalLimit"] = JsonSerializer.SerializeToElement(1000)
                }
            });
        binding.SetDraftRevision(revision.Id);

        var definition = new WorkflowDefinition(source.TenantId, "ExpenseApproval", 1, "Review");
        definition.SetContextLineage(source.Id, revision.Id, Guid.NewGuid());
        var instanceId = Guid.NewGuid();
        var snapshot = new WorkflowContextSnapshot(
            instanceId,
            source.TenantId,
            revision.Id,
            new Dictionary<string, JsonElement>
            {
                ["Amount"] = JsonSerializer.SerializeToElement(250)
            });

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        context.WorkflowContextSnapshots.Add(snapshot);
        await context.SaveChangesAsync();

        var service = new WorkflowExecutionContextService(new UnitOfWork(context));
        var prepared = await service.PrepareForInstanceAsync(
            source.TenantId,
            definition,
            instanceId,
            null,
            null);

        Assert.NotNull(prepared);
        Assert.Equal(250, ((JsonElement)prepared!.Payload["Amount"]).GetInt32());
        Assert.Equal(1000, ((JsonElement)prepared.Payload["ApprovalLimit"]).GetInt32());
        Assert.Empty(prepared.Delta);
    }

    [Fact]
    public async Task ExecutionContext_DiscardsSourceFieldsWithoutExplicitMappings()
    {
        var source = CreateSource();
        var binding = new WorkflowContextBinding(source.TenantId, "Expense", "ExpenseApproval");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition
            {
                EntityType = "ExpenseEntity",
                ConditionParameters = new Dictionary<string, JsonElement>
                {
                    ["ApprovalLimit"] = JsonSerializer.SerializeToElement(1000)
                }
            });
        var definition = new WorkflowDefinition(source.TenantId, "ExpenseApproval", 1, "Review");

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        var service = new WorkflowExecutionContextService(new UnitOfWork(context));

        var prepared = service.PrepareInitial(
            new ActiveWorkflowContextBinding(binding, revision, definition, source),
            new { Amount = 250, RawSecret = "must-not-be-retained" });

        Assert.DoesNotContain("Amount", prepared.Delta.Keys);
        Assert.DoesNotContain("RawSecret", prepared.Delta.Keys);
        Assert.Equal(1000, prepared.Delta["ApprovalLimit"].GetInt32());
    }

    [Fact]
    public async Task Activation_RejectsExistingEventWithDifferentRuntimeSemantics()
    {
        var source = CreateSource();
        source.Definition.Workflow.Steps.Single().OnEntry.Clear();
        Assert.True(new WorkflowClassManager().Publish(source).IsValid);

        var binding = new WorkflowContextBinding(source.TenantId, "Expense", "ExpenseApproval");
        var revision = CreateRevision(binding, sourceId: source.Id);
        binding.SetDraftRevision(revision.Id);

        var conflictingEvent = new EventDefinition(
            "EVT-EXP-APPROVE",
            source.TenantId,
            "Existing approval",
            string.Empty,
            "ExpenseEntity",
            EventCategory.Human,
            payloadSchema: """{"type":"object"}""",
            isTerminal: true);
        conflictingEvent.Publish();

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        context.EventDefinitions.Add(conflictingEvent);
        context.Roles.Add(new FlowOS.Security.Models.Role(source.TenantId, "FinanceManager"));
        await context.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(context);
        var validator = new WorkflowContextBindingValidator(
            unitOfWork,
            new Mock<IPolicyDecisionPluginRegistry>().Object);
        var materializer = new WorkflowContextMaterializer(unitOfWork, validator);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => materializer.ActivateAsync(binding, revision));
    }

    [Fact]
    public async Task ContextSimulation_DraftAppliesEventMapping_AndDeniedDeltaIsTransactional()
    {
        var source = CreateSource();
        source.Definition.Workflow.Steps.Single().OnEntry.Clear();
        Assert.True(new WorkflowClassManager().Publish(source).IsValid);

        var binding = new WorkflowContextBinding(source.TenantId, "Expense", "ExpenseApproval");
        var revision = CreateSimulationRevision(binding, source);
        binding.SetDraftRevision(revision.Id);

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        context.Roles.Add(new FlowOS.Security.Models.Role(source.TenantId, "FinanceManager"));
        await context.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(context);
        var pluginRegistry = new Mock<IPolicyDecisionPluginRegistry>().Object;
        var service = new WorkflowContextSimulationService(
            unitOfWork,
            new WorkflowContextBindingValidator(unitOfWork, pluginRegistry),
            new WorkflowExecutionContextService(unitOfWork),
            new FlowOS.Workflows.Engine.WorkflowEngine(new FlowOS.StateMachines.Engine.StateMachineEngine()));
        var trackedEntriesBefore = context.ChangeTracker.Entries().Count();

        var result = await service.SimulateAsync(source.TenantId, new WorkflowContextSimulationRequest(
            ContextBindingId: binding.Id,
            Revision: "draft",
            InitialPayload: new { expense = new { amount = 500 } },
            Roles: ["FinanceManager"],
            Events:
            [
                new WorkflowContextSimulationEventRequest(
                    "EVT-EXP-APPROVE",
                    new { decision = new { amount = 1200 } })
            ]));

        var denied = Assert.Single(result.Trace, item => !item.IsAllowed);
        Assert.Equal("Denied", result.Status);
        Assert.Equal("EVT-APPROVE", denied.CanonicalEventType);
        Assert.Equal(1200L, denied.CanonicalDelta["Amount"]);
        Assert.Equal(500L, denied.ContextBefore["Amount"]);
        Assert.Equal(500L, denied.ContextAfter["Amount"]);
        Assert.Equal(500L, result.FinalCanonicalContext["Amount"]);
        Assert.True(result.SideEffectsSuppressed);
        Assert.False(result.IsPersistedRuntime);
        Assert.Equal(trackedEntriesBefore, context.ChangeTracker.Entries().Count());
        Assert.Empty(context.WorkflowInstances);
        Assert.Empty(context.WorkflowContextSnapshots);
    }

    [Fact]
    public async Task ContextSimulation_ActiveUsesPinnedRuntime_AndCompletesWithMappedRole()
    {
        var source = CreateSource();
        source.Definition.Workflow.Steps.Single().OnEntry.Clear();
        Assert.True(new WorkflowClassManager().Publish(source).IsValid);

        var binding = new WorkflowContextBinding(source.TenantId, "Expense", "ExpenseApproval");
        var revision = CreateSimulationRevision(binding, source);
        binding.SetDraftRevision(revision.Id);

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        context.Roles.Add(new FlowOS.Security.Models.Role(source.TenantId, "FinanceManager"));
        await context.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(context);
        var pluginRegistry = new Mock<IPolicyDecisionPluginRegistry>().Object;
        var validator = new WorkflowContextBindingValidator(unitOfWork, pluginRegistry);
        var package = await new WorkflowContextMaterializer(unitOfWork, validator)
            .ActivateAsync(binding, revision);
        context.ChangeTracker.Clear();

        var result = await new WorkflowContextSimulationService(
                unitOfWork,
                validator,
                new WorkflowExecutionContextService(unitOfWork),
                new FlowOS.Workflows.Engine.WorkflowEngine(new FlowOS.StateMachines.Engine.StateMachineEngine()))
            .SimulateAsync(source.TenantId, new WorkflowContextSimulationRequest(
                ContextBindingId: binding.Id,
                Revision: "active",
                InitialPayload: new { expense = new { amount = 500 } },
                Roles: ["FinanceManager"],
                Events:
                [
                    new WorkflowContextSimulationEventRequest(
                        "EVT-EXP-APPROVE",
                        new { decision = new { amount = 500 } })
                ]));

        Assert.Equal("Completed", result.Status);
        Assert.True(result.IsPersistedRuntime);
        Assert.Equal(package.WorkflowDefinition.Id, revision.WorkflowDefinitionId);
        Assert.Equal(revision.Id, result.ContextBindingRevisionId);
        Assert.Contains(result.Trace, item =>
            item.EventType == "EVT-EXP-APPROVE" &&
            item.CanonicalEventType == "EVT-APPROVE" &&
            item.IsAllowed);
        Assert.Empty(context.WorkflowInstances);
        Assert.Empty(context.WorkflowContextSnapshots);
    }

    [Fact]
    public async Task ContextSimulation_DecisionAutoRoute_AppliesQuoteApprovedAsStateOnlyThenContinues()
    {
        var source = CreateRepairSource();
        Assert.True(new WorkflowClassManager().Publish(source).IsValid);

        var binding = new WorkflowContextBinding(source.TenantId, "ServiceRepairContextSimNoRoles", "ServiceRepairBinding");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition
            {
                EntityType = "ServiceRepairJob",
                InputMapping = new Dictionary<string, string>
                {
                    ["JobId"] = "jobId"
                }
            });
        binding.SetDraftRevision(revision.Id);

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        await context.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(context);
        var result = await new WorkflowContextSimulationService(
                unitOfWork,
                new WorkflowContextBindingValidator(unitOfWork, new Mock<IPolicyDecisionPluginRegistry>().Object),
                new WorkflowExecutionContextService(unitOfWork),
                new FlowOS.Workflows.Engine.WorkflowEngine(new FlowOS.StateMachines.Engine.StateMachineEngine()))
            .SimulateAsync(source.TenantId, new WorkflowContextSimulationRequest(
                ContextBindingId: binding.Id,
                Revision: "draft",
                InitialPayload: new { jobId = "CTX-SIM-NOROLES-001" },
                Roles: Array.Empty<string>(),
                Events:
                [
                    new WorkflowContextSimulationEventRequest("JOB_REQUESTED"),
                    new WorkflowContextSimulationEventRequest("QUOTE_APPROVED"),
                    new WorkflowContextSimulationEventRequest("MATERIALS_REQUIRED")
                ]));

        Assert.NotEqual("Denied", result.Status);
        Assert.Contains(result.Trace, item =>
            item.EventType == "QUOTE_APPROVED" &&
            item.IsAllowed &&
            item.FromStepId == "MaterialDecision" &&
            item.ToStepId == "MaterialDecision" &&
            item.FromState == "Assigned" &&
            item.ToState == "Quoted");
        Assert.Contains(result.Trace, item =>
            item.EventType == "MATERIALS_REQUIRED" &&
            item.IsAllowed &&
            item.FromState == "Quoted");
        Assert.Equal("RepairInProgress", result.CurrentState);
    }

    [Fact]
    public void ContextCompiler_EmptyEventName_UsesEventId()
    {
        var source = CreateRepairSource();
        source.UpdateDraft(
            source.Name,
            source.Version,
            source.Definition with
            {
                Events = source.Definition.Events.Select(item => item with { Name = string.Empty }).ToList()
            });
        var binding = new WorkflowContextBinding(source.TenantId, "Repair", "RepairBinding");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition { EntityType = "RepairJobContext" });

        var package = WorkflowClassCompiler.MapToContextRuntimePackage(source, binding, revision);

        Assert.All(package.EventDefinitions, item => Assert.False(string.IsNullOrWhiteSpace(item.Name)));
        Assert.Contains(package.EventDefinitions, item => item.EventId == "JOB_REQUESTED" && item.Name == "JOB_REQUESTED");
        Assert.Equal("RepairJobContext", package.StateMachineDefinition.EntityType);
    }

    [Fact]
    public async Task CreateBinding_AcceptsDraftSource_AndSimulationSkipsMissingTenantRoles()
    {
        var source = CreateSource();
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        context.WorkflowClasses.Add(source);
        await context.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(context);
        var validator = new WorkflowContextBindingValidator(
            unitOfWork,
            new Mock<IPolicyDecisionPluginRegistry>().Object);
        var handler = new WorkflowContextBindingHandlers(
            unitOfWork,
            validator,
            new WorkflowContextMaterializer(unitOfWork, validator));

        var created = await handler.Handle(
            new CreateWorkflowContextBindingCommand(
                source.TenantId,
                source.Id,
                "ExpenseDraftSim",
                "ExpenseDraftSimBinding",
                new WorkflowContextBindingDefinition
                {
                    EntityType = "ExpenseEntity",
                    RoleOverrides = new Dictionary<string, string> { ["Approver"] = "FinanceManager" },
                    InputMapping = new Dictionary<string, string> { ["Amount"] = "expense.amount" },
                    ConditionParameters = new Dictionary<string, JsonElement>
                    {
                        ["ApprovalLimit"] = JsonSerializer.SerializeToElement(1000)
                    }
                }),
            default);

        Assert.NotNull(created.DraftRevisionId);

        var validation = await handler.Handle(
            new ValidateWorkflowContextBindingCommand(source.TenantId, created.Id),
            default);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, item => item.Code == "CTX-SOURCE-002");
        Assert.Contains(validation.Errors, item => item.Code == "CTX-ROLE-002");

        var simulation = await new WorkflowContextSimulationService(
                unitOfWork,
                validator,
                new WorkflowExecutionContextService(unitOfWork),
                new FlowOS.Workflows.Engine.WorkflowEngine(new FlowOS.StateMachines.Engine.StateMachineEngine()))
            .SimulateAsync(
                source.TenantId,
                new WorkflowContextSimulationRequest(
                    ContextBindingId: created.Id,
                    Revision: "draft",
                    InitialPayload: new { expense = new { amount = 125 } },
                    Roles: ["FinanceManager"],
                    Events: [new WorkflowContextSimulationEventRequest("EVT-APPROVE")]));

        Assert.NotEqual("Denied", simulation.Status);
        Assert.Equal("Completed", simulation.Status);
    }

    [Fact]
    public async Task ContextSimulation_HumanTaskSla_FiresRemindersBeforeCompletingEvent()
    {
        var source = CreateSlaReminderSource();
        var binding = new WorkflowContextBinding(source.TenantId, "QuoteSla", "QuoteSlaBinding");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition
            {
                EntityType = "ServiceRepairJob",
                InputMapping = new Dictionary<string, string> { ["JobId"] = "job.id" }
            });
        binding.SetDraftRevision(revision.Id);

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        await context.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(context);
        var pluginRegistry = new Mock<IPolicyDecisionPluginRegistry>().Object;
        var result = await new WorkflowContextSimulationService(
                unitOfWork,
                new WorkflowContextBindingValidator(unitOfWork, pluginRegistry),
                new WorkflowExecutionContextService(unitOfWork),
                new FlowOS.Workflows.Engine.WorkflowEngine(new FlowOS.StateMachines.Engine.StateMachineEngine()))
            .SimulateAsync(
                source.TenantId,
                new WorkflowContextSimulationRequest(
                    ContextBindingId: binding.Id,
                    Revision: "draft",
                    InitialPayload: new { job = new { id = "JOB-1" } },
                    Roles: ["Customer"],
                    Events: [new WorkflowContextSimulationEventRequest("QUOTE_APPROVED")]));

        Assert.Equal("Completed", result.Status);
        Assert.Equal("Quoted", result.CurrentState);
        Assert.Equal(2, result.Trace.Count(item =>
            item.EventType == "QUOTE_REMINDER_SENT" &&
            item.IsAllowed &&
            item.Outcome.Contains("[SLA Reminder Fired]")));
        Assert.DoesNotContain(result.Trace, item => item.EventType == "QUOTE_RESPONSE_OVERDUE");
        Assert.Contains(result.Trace[0].PendingWork, item => item.Kind == "SlaReminder");
    }

    [Fact]
    public async Task ContextSimulation_HumanTaskSla_AutoAdvanceTimersFiresTimeout()
    {
        var source = CreateSlaReminderSource(includeTimeoutNextStep: true);
        var binding = new WorkflowContextBinding(source.TenantId, "QuoteSlaOverdue", "QuoteSlaOverdueBinding");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition { EntityType = "ServiceRepairJob" });
        binding.SetDraftRevision(revision.Id);

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        await context.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(context);
        var pluginRegistry = new Mock<IPolicyDecisionPluginRegistry>().Object;
        var result = await new WorkflowContextSimulationService(
                unitOfWork,
                new WorkflowContextBindingValidator(unitOfWork, pluginRegistry),
                new WorkflowExecutionContextService(unitOfWork),
                new FlowOS.Workflows.Engine.WorkflowEngine(new FlowOS.StateMachines.Engine.StateMachineEngine()))
            .SimulateAsync(
                source.TenantId,
                new WorkflowContextSimulationRequest(
                    ContextBindingId: binding.Id,
                    Revision: "draft",
                    AutoAdvanceTimers: true));

        Assert.Equal("Completed", result.Status);
        Assert.Equal("Overdue", result.CurrentState);
        Assert.Contains(result.Trace, item =>
            item.EventType == "QUOTE_RESPONSE_OVERDUE" && item.IsAllowed);
    }

    [Fact]
    public async Task Activation_AllowsSameEventId_WhenOnlyEntityTypeDiffers()
    {
        var source = CreateRepairSource();
        source.Definition.Workflow.Steps.Single(item => item.StepId == "ApproveQuote").OnEntry.Clear();
        Assert.True(new WorkflowClassManager().Publish(source).IsValid);

        var binding = new WorkflowContextBinding(source.TenantId, "RepairContext", "RepairContextBinding");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition { EntityType = "RepairJobContext" });
        binding.SetDraftRevision(revision.Id);

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        context.WorkflowClasses.Add(source);
        context.WorkflowContextBindings.Add(binding);
        context.WorkflowContextBindingRevisions.Add(revision);
        foreach (var evt in source.Definition.Events)
        {
            var existing = new EventDefinition(
                evt.EventId,
                source.TenantId,
                string.IsNullOrWhiteSpace(evt.Name) ? evt.EventId : evt.Name,
                evt.Description,
                "ServiceRepairJob",
                evt.Category,
                1,
                evt.PayloadSchema,
                evt.IsTerminal);
            existing.Publish();
            context.EventDefinitions.Add(existing);
        }
        await context.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(context);
        var validator = new WorkflowContextBindingValidator(
            unitOfWork,
            new Mock<IPolicyDecisionPluginRegistry>().Object);
        var materializer = new WorkflowContextMaterializer(unitOfWork, validator);

        var package = await materializer.ActivateAsync(binding, revision);

        Assert.Equal("RepairJobContext", package.StateMachineDefinition.EntityType);
        Assert.Equal(WorkflowContextBindingStatus.Active, binding.Status);
    }

    [Fact]
    public async Task ContextSimulation_RejectsUnboundedScenarioBeforeRepositoryAccess()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);
        var unitOfWork = new UnitOfWork(context);
        var service = new WorkflowContextSimulationService(
            unitOfWork,
            new WorkflowContextBindingValidator(unitOfWork, new Mock<IPolicyDecisionPluginRegistry>().Object),
            new WorkflowExecutionContextService(unitOfWork),
            new FlowOS.Workflows.Engine.WorkflowEngine(new FlowOS.StateMachines.Engine.StateMachineEngine()));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SimulateAsync(
            Guid.NewGuid(),
            new WorkflowContextSimulationRequest(
                ContextBindingId: Guid.NewGuid(),
                MaxSteps: 101)));
    }

    private static WorkflowContextBindingRevision CreateSimulationRevision(
        WorkflowContextBinding binding,
        WorkflowClass source)
        => new(
            binding.Id,
            1,
            source.Id,
            source.Version,
            new WorkflowContextBindingDefinition
            {
                EntityType = "ExpenseEntity",
                EventAliases = new Dictionary<string, string>
                {
                    ["EVT-APPROVE"] = "EVT-EXP-APPROVE"
                },
                RoleOverrides = new Dictionary<string, string>
                {
                    ["Approver"] = "FinanceManager"
                },
                InputMapping = new Dictionary<string, string>
                {
                    ["Amount"] = "expense.amount"
                },
                EventInputMappings = new Dictionary<string, Dictionary<string, string>>
                {
                    ["EVT-APPROVE"] = new()
                    {
                        ["Amount"] = "decision.amount"
                    }
                },
                ConditionParameters = new Dictionary<string, JsonElement>
                {
                    ["ApprovalLimit"] = JsonSerializer.SerializeToElement(1000)
                },
                SourcePayloadSchema = """
                    {"type":"object","required":["expense"],"properties":{"expense":{"type":"object","required":["amount"],"properties":{"amount":{"type":"number"}}}}}
                    """,
                EventSourcePayloadSchemas = new Dictionary<string, string>
                {
                    ["EVT-APPROVE"] = """
                        {"type":"object","required":["decision"],"properties":{"decision":{"type":"object","required":["amount"],"properties":{"amount":{"type":"number"}}}}}
                        """
                }
            });

    private static WorkflowContextBindingRevision CreateRevision(
        WorkflowContextBinding binding,
        int revision = 1,
        Guid? sourceId = null)
        => new(
            binding.Id,
            revision,
            sourceId ?? Guid.NewGuid(),
            "1.0.0",
            new WorkflowContextBindingDefinition
            {
                EntityType = "ExpenseEntity",
                EventAliases = new Dictionary<string, string>
                {
                    ["EVT-APPROVE"] = "EVT-EXP-APPROVE"
                },
                RoleOverrides = new Dictionary<string, string>
                {
                    ["Approver"] = "FinanceManager"
                }
            });

    private static WorkflowClass CreateRepairSource()
    {
        var tenantId = Guid.NewGuid();
        return new WorkflowClass(
            tenantId,
            "HomeServiceRepairContextSimulator",
            "1.0.0",
            new WorkflowClassBlueprint
            {
                ContextSchema = """{"type":"object","properties":{"JobId":{"type":"string"}}}""",
                Events =
                [
                    new EventBlueprint { EventId = "JOB_REQUESTED", Name = "Job requested" },
                    new EventBlueprint { EventId = "QUOTE_APPROVED", Name = "Quote approved" },
                    new EventBlueprint { EventId = "MATERIALS_REQUIRED", Name = "Materials required" }
                ],
                StateMachine = new StateMachineBlueprint
                {
                    EntityType = "ServiceRepairJob",
                    InitialState = "Requested",
                    States = ["Requested", "Assigned", "Quoted", "RepairInProgress"],
                    Transitions =
                    [
                        new TransitionBlueprint { FromState = "Requested", ToState = "Assigned", EventId = "JOB_REQUESTED" },
                        new TransitionBlueprint { FromState = "Assigned", ToState = "Quoted", EventId = "QUOTE_APPROVED" },
                        new TransitionBlueprint { FromState = "Quoted", ToState = "RepairInProgress", EventId = "MATERIALS_REQUIRED" }
                    ]
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "IntakeRequest",
                    Steps =
                    [
                        new StepBlueprint
                        {
                            StepId = "IntakeRequest",
                            StepType = "HumanTask",
                            NextSteps = new Dictionary<string, string> { ["JOB_REQUESTED"] = "ApproveQuote" }
                        },
                        new StepBlueprint
                        {
                            StepId = "ApproveQuote",
                            StepType = "Decision",
                            Conditions = new Dictionary<string, string> { ["Default"] = "MaterialDecision" }
                        },
                        new StepBlueprint
                        {
                            StepId = "MaterialDecision",
                            StepType = "HumanTask",
                            NextSteps = new Dictionary<string, string> { ["MATERIALS_REQUIRED"] = "CloseJob" }
                        },
                        new StepBlueprint
                        {
                            StepId = "CloseJob",
                            StepType = "Command",
                            NextSteps = new Dictionary<string, string> { ["Default"] = "END" }
                        }
                    ]
                }
            });
    }

    private static WorkflowClass CreateSlaReminderSource(bool includeTimeoutNextStep = false)
    {
        var nextSteps = new Dictionary<string, string> { ["QUOTE_APPROVED"] = "END" };
        if (includeTimeoutNextStep)
            nextSteps["QUOTE_RESPONSE_OVERDUE"] = "END";

        var tenantId = Guid.NewGuid();
        return new WorkflowClass(
            tenantId,
            "QuoteSlaReminderSimulator",
            "1.0.0",
            new WorkflowClassBlueprint
            {
                ContextSchema = """{"type":"object","properties":{"JobId":{"type":"string"}}}""",
                Events =
                [
                    new EventBlueprint { EventId = "QUOTE_REMINDER_SENT", Name = "Quote reminder" },
                    new EventBlueprint { EventId = "QUOTE_APPROVED", Name = "Quote approved" },
                    new EventBlueprint { EventId = "QUOTE_RESPONSE_OVERDUE", Name = "Quote overdue" }
                ],
                StateMachine = new StateMachineBlueprint
                {
                    EntityType = "ServiceRepairJob",
                    InitialState = "Assigned",
                    States = ["Assigned", "Quoted", "Overdue"],
                    Transitions =
                    [
                        new TransitionBlueprint { FromState = "Assigned", ToState = "Quoted", EventId = "QUOTE_APPROVED" },
                        new TransitionBlueprint { FromState = "Assigned", ToState = "Overdue", EventId = "QUOTE_RESPONSE_OVERDUE" }
                    ]
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "ApproveQuote",
                    Steps =
                    [
                        new StepBlueprint
                        {
                            StepId = "ApproveQuote",
                            StepType = "HumanTask",
                            RequiredRoles = ["Customer"],
                            Sla = new StepSlaBlueprint
                            {
                                Duration = "24h",
                                TimeoutEvent = "QUOTE_RESPONSE_OVERDUE",
                                Reminders =
                                [
                                    new() { Duration = "2h", TriggerEvent = "QUOTE_REMINDER_SENT" },
                                    new() { Duration = "12h", TriggerEvent = "QUOTE_REMINDER_SENT" }
                                ]
                            },
                            NextSteps = nextSteps
                        }
                    ]
                }
            });
    }

    private static WorkflowClass CreateSource()
    {
        var tenantId = Guid.NewGuid();
        return new WorkflowClass(
            tenantId,
            "ReusableApproval",
            "1.0.0",
            new WorkflowClassBlueprint
            {
                ContextSchema = """{"type":"object","properties":{"Amount":{"type":"number"},"ApprovalLimit":{"type":"number"}}}""",
                Events =
                [
                    new EventBlueprint
                    {
                        EventId = "EVT-APPROVE",
                        Name = "Approve",
                        PayloadSchema = """{"type":"object"}"""
                    }
                ],
                StateMachine = new StateMachineBlueprint
                {
                    EntityType = "ApprovalSubject",
                    InitialState = "Pending",
                    States = ["Pending", "Approved"],
                    Transitions =
                    [
                        new TransitionBlueprint
                        {
                            FromState = "Pending",
                            ToState = "Approved",
                            EventId = "EVT-APPROVE",
                            Condition = "Amount <= ApprovalLimit",
                            Constraints = new Dictionary<string, string> { ["Role"] = "Approver" }
                        }
                    ]
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Review",
                    Steps =
                    [
                        new StepBlueprint
                        {
                            StepId = "Review",
                            StepType = "HumanTask",
                            RequiredRoles = ["Approver"],
                            NextSteps = new Dictionary<string, string> { ["EVT-APPROVE"] = "END" },
                            Sla = new StepSlaBlueprint
                            {
                                Duration = "24h",
                                TimeoutEvent = "EVT-APPROVE",
                                EscalationRole = "Approver"
                            },
                            OnEntry =
                            [
                                new StepActionBlueprint
                                {
                                    ActionType = "PublishEvent",
                                    Target = "EVT-APPROVE",
                                    Capability = "event.publish.EVT-APPROVE"
                                }
                            ]
                        }
                    ]
                },
                Roles = [new RoleBlueprint { Name = "Approver" }],
                Capabilities = [new CapabilityBlueprint { Code = "event.publish.EVT-APPROVE" }]
            });
    }
}
