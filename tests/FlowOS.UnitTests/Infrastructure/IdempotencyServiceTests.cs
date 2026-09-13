using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.Handlers;
using FlowOS.Core.Interfaces;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Infrastructure.Services;
using FlowOS.Security.Interfaces;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using FlowOS.StateMachines.Engine;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class IdempotencyServiceTests
{
    [Fact]
    public async Task IdempotencyService_Completes_And_Returns_Cached_Result()
    {
        var db = BuildDb();
        var service = new IdempotencyService(db);
        var tenantId = Guid.NewGuid();

        var begun = await service.TryBeginAsync(tenantId, "publish_event", "evt-key-001");
        await service.CompleteAsync(tenantId, "publish_event", "evt-key-001", true);
        var cached = await service.TryGetCompletedResultAsync<bool>(tenantId, "publish_event", "evt-key-001");

        Assert.True(begun);
        Assert.True(cached.Found);
        Assert.True(cached.Result);
    }

    [Fact]
    public async Task WorkflowHandler_StartWorkflow_Reuses_Result_For_Same_Idempotency_Key()
    {
        var db = BuildDb();
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "Startable", 1, "Start");
        definition.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command));
        definition.Publish();
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var mockEventRegistry = new Mock<IEventRegistry>();
        var mockCurrentUser = new Mock<ICurrentUser>();
        var mockCapability = new Mock<ICapabilityService>();
        var idempotency = new IdempotencyService(db);
        var handler = new WorkflowCommandHandlers(
            new UnitOfWork(db),
            mockEventRegistry.Object,
            mockCurrentUser.Object,
            mockCapability.Object,
            new WorkflowEngine(new StateMachineEngine()),
            null,
            null,
            idempotency);

        var command = new StartWorkflowCommand(
            TenantId: tenantId,
            WorkflowDefinitionId: definition.Id,
            WorkflowName: null,
            Version: null,
            WorkflowClassId: Guid.Empty,
            InitialStepId: null,
            CorrelationId: null,
            IdempotencyKey: "start-order-1001");

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first, second);
        var instances = await db.WorkflowInstances.ToListAsync();
        Assert.Single(instances);
    }

    private static FlowOSDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FlowOSDbContext(options);
    }
}
