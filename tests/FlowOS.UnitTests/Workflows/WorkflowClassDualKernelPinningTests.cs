using FlowOS.API.Services;
using FlowOS.Application.Commands;
using FlowOS.Application.Handlers;
using FlowOS.Application.Services;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Infrastructure.Services;
using FlowOS.Security.Interfaces;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowClassDualKernelPinningTests
{
    [Fact]
    public void RuntimePackage_PinsPublishedWorkAndLawToSourceClass()
    {
        var workflowClass = CreatePublishedClass(Guid.NewGuid(), "PinnedFlow");

        var package = WorkflowClassCompiler.MapToRuntimePackage(workflowClass);

        Assert.Equal(workflowClass.Id, package.WorkflowDefinition.SourceWorkflowClassId);
        Assert.Null(package.WorkflowDefinition.ContextBindingRevisionId);
        Assert.Equal(package.StateMachineDefinition.Id, package.WorkflowDefinition.StateMachineDefinitionId);
        Assert.Equal(WorkflowStatus.Published, package.WorkflowDefinition.Status);
        Assert.Equal(StateMachineStatus.Published, package.StateMachineDefinition.Status);
        Assert.Equal("Draft", package.StateMachineDefinition.InitialState);
        Assert.Single(package.StateMachineDefinition.Transitions);
    }

    [Fact]
    public async Task FlagshipSeed_PinsDefinitionsWithoutDuplicatingLaw()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext($"pinned-seed-{tenantId}");

        await DataSeeder.SeedFlagshipWorkflowsAsync(context, tenantId);
        var firstLawCount = await context.StateMachineDefinitions.CountAsync();

        var workflowClass = await context.WorkflowClasses.SingleAsync(item =>
            item.TenantId == tenantId && item.Name == "QuoteAutoReview");
        var definition = await context.WorkflowDefinitions.SingleAsync(item =>
            item.TenantId == tenantId && item.Name == "QuoteAutoReview");
        var law = await context.StateMachineDefinitions.SingleAsync(item =>
            item.Id == definition.StateMachineDefinitionId);

        Assert.Equal(workflowClass.Id, definition.SourceWorkflowClassId);
        Assert.Null(definition.ContextBindingRevisionId);
        Assert.Equal(StateMachineStatus.Published, law.Status);
        Assert.Equal("Draft", law.InitialState);

        await DataSeeder.SeedFlagshipWorkflowsAsync(context, tenantId);

        Assert.Equal(firstLawCount, await context.StateMachineDefinitions.CountAsync());
    }

    [Fact]
    public async Task PublicQuote_StartByName_UsesPinnedClassAndLawInitialState()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext($"public-quote-start-{tenantId}");
        await DataSeeder.SeedFlagshipWorkflowsAsync(context, tenantId);

        var handler = CreateHandler(context);
        var instanceId = await handler.Handle(
            new StartWorkflowCommand(TenantId: tenantId, WorkflowName: "QuoteAutoReview"),
            CancellationToken.None);

        var instance = await context.WorkflowInstances.SingleAsync(item => item.Id == instanceId);
        var workflowClass = await context.WorkflowClasses.SingleAsync(item =>
            item.TenantId == tenantId && item.Name == "QuoteAutoReview");

        Assert.Equal(WorkflowClassStatus.Public, workflowClass.Status);
        Assert.Equal(workflowClass.Id, instance.WorkflowClassId);
        Assert.NotEqual(Guid.Empty, instance.WorkflowClassId);
        Assert.Equal("Draft", instance.CurrentState);
    }

    [Fact]
    public async Task PublishEvent_PinnedDefinitionWithMissingLaw_FailsClosedWhenInstanceClassIdIsEmpty()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext($"missing-pinned-law-{tenantId}");
        var workflowClass = CreatePublishedClass(tenantId, "MissingLawFlow");
        var definition = WorkflowClassCompiler.MapToRuntimeDefinition(workflowClass);
        definition.SetClassLineage(workflowClass.Id, Guid.NewGuid());
        var instance = new WorkflowInstance(
            tenantId,
            definition.Id,
            Guid.Empty,
            definition.Version,
            definition.StartStepId,
            initialState: "Draft");

        context.WorkflowClasses.Add(workflowClass);
        context.WorkflowDefinitions.Add(definition);
        context.WorkflowInstances.Add(instance);
        await context.SaveChangesAsync();

        var handler = CreateHandler(context);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new PublishEventCommand(tenantId, instance.Id, "EVT-GO"),
                CancellationToken.None));

        Assert.Contains("fail-closed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Start", instance.CurrentStepId);
    }

    [Fact]
    public async Task StartupBackfill_RepairsLegacyDefinitionInMemoryAndIsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext($"lineage-backfill-{tenantId}");
        var workflowClass = CreatePublishedClass(tenantId, "LegacyPinnedFlow");
        var legacyDefinition = WorkflowClassCompiler.MapToRuntimeDefinition(workflowClass);
        context.WorkflowClasses.Add(workflowClass);
        context.WorkflowDefinitions.Add(legacyDefinition);
        await context.SaveChangesAsync();

        var service = new WorkflowDefinitionLineageBackfillService(
            context,
            NullLogger<WorkflowDefinitionLineageBackfillService>.Instance);

        var first = await service.BackfillAsync();
        var lawCount = await context.StateMachineDefinitions.CountAsync();
        var second = await service.BackfillAsync();

        Assert.Equal(1, first.DefinitionsRepaired);
        Assert.Equal(1, first.StateMachinesCreated);
        Assert.Equal(workflowClass.Id, legacyDefinition.SourceWorkflowClassId);
        Assert.NotNull(legacyDefinition.StateMachineDefinitionId);
        Assert.Equal(1, lawCount);
        Assert.Equal(0, second.DefinitionsRepaired);
        Assert.Equal(0, second.StateMachinesCreated);
        Assert.Equal(lawCount, await context.StateMachineDefinitions.CountAsync());
    }

    private static FlowOSDbContext CreateContext(string databaseName)
        => new(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static WorkflowCommandHandlers CreateHandler(FlowOSDbContext context)
    {
        var eventRegistry = new Mock<IEventRegistry>();
        eventRegistry
            .Setup(registry => registry.ExistsAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(true);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(user => user.Id).Returns(string.Empty);
        currentUser.Setup(user => user.Roles).Returns(new List<string>());

        return new WorkflowCommandHandlers(
            new UnitOfWork(context),
            eventRegistry.Object,
            currentUser.Object,
            Mock.Of<ICapabilityService>(),
            new WorkflowEngine(new StateMachineEngine()));
    }

    private static WorkflowClass CreatePublishedClass(Guid tenantId, string name)
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events =
            [
                new EventBlueprint { EventId = "EVT-GO", Name = "Go" }
            ],
            StateMachine = new StateMachineBlueprint
            {
                EntityType = name,
                InitialState = "Draft",
                States = ["Draft", "Active"],
                Transitions =
                [
                    new TransitionBlueprint
                    {
                        FromState = "Draft",
                        ToState = "Active",
                        EventId = "EVT-GO"
                    }
                ]
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Start",
                Steps =
                [
                    new StepBlueprint
                    {
                        StepId = "Start",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { ["EVT-GO"] = "End" }
                    },
                    new StepBlueprint { StepId = "End", StepType = "End" }
                ]
            }
        };
        var workflowClass = new WorkflowClass(tenantId, name, "1.0.0", blueprint);
        var result = new WorkflowClassManager().Publish(workflowClass);
        Assert.True(result.IsValid);
        return workflowClass;
    }
}
