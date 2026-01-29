using System;
using System.Linq;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.StateMachines.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class TestFlowOSDbContext : FlowOSDbContext
{
    public TestFlowOSDbContext(DbContextOptions<FlowOSDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Register the TestDomainEvent so EF Core knows about it
        modelBuilder.Entity<WorkflowRecoveryTests.TestDomainEvent>();
    }
}

public class WorkflowRecoveryTests
{
    private readonly DbContextOptions<FlowOSDbContext> _dbOptions;

    public WorkflowRecoveryTests()
    {
        _dbOptions = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName: "FlowOS_Recovery_Test")
            .Options;
    }

    private WorkflowDefinition CreateSimpleWorkflow(Guid tenantId)
    {
        var def = new WorkflowDefinition(tenantId, "RecoveryFlow");
        var step1 = new WorkflowStepDefinition("Step1", WorkflowStepType.Command);
        step1.NextSteps.Add("EventA", "Step2");
        def.AddStep(step1);
        
        var step2 = new WorkflowStepDefinition("Step2", WorkflowStepType.HumanTask);
        step2.NextSteps.Add("EventB", "END");
        def.AddStep(step2);

        def.Publish();
        return def;
    }

    [Fact]
    public void SimulateProcessKill_ShouldRecoverWorkflowState()
    {
        var tenantId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var def = CreateSimpleWorkflow(tenantId);
        Guid workflowId;

        // 1. Simulate "Process A" - Create and Advance
        using (var context = new TestFlowOSDbContext(_dbOptions))
        {
            var instance = new WorkflowInstance(tenantId, def.Id, def.Version, "Step1", correlationId);
            workflowId = instance.Id;

            // Advance to Step 2
            instance.AdvanceTo("Step2");
            instance.Wait(); // Simulate human task wait

            context.WorkflowInstances.Add(instance);
            context.SaveChanges();
        }

        // 2. Simulate "Process B" - Reload (Process Kill simulation)
        using (var context = new TestFlowOSDbContext(_dbOptions))
        {
            var loadedInstance = context.WorkflowInstances
                .FirstOrDefault(w => w.Id == workflowId);

            Assert.NotNull(loadedInstance);
            Assert.Equal("Step2", loadedInstance.CurrentStepId);
            Assert.Equal(WorkflowInstanceStatus.Waiting, loadedInstance.Status);
            Assert.Equal(correlationId, loadedInstance.CorrelationId);
            Assert.Equal(tenantId, loadedInstance.TenantId);

            // 3. Continue Workflow
            var engine = new WorkflowEngine();
            var evt = new TestDomainEvent(tenantId, "EventB");
            
            // In a real scenario, we'd load the definition too
            var result = engine.Advance(loadedInstance, def, evt, new FlowOS.StateMachines.Models.ExecutionContext());

            Assert.True(result.Success);
            Assert.Equal(WorkflowInstanceStatus.Completed, loadedInstance.Status);

            context.SaveChanges();
        }

        // 4. Verify Final State
        using (var context = new TestFlowOSDbContext(_dbOptions))
        {
            var finalInstance = context.WorkflowInstances.Find(workflowId);
            Assert.Equal(WorkflowInstanceStatus.Completed, finalInstance!.Status);
        }
    }
    
    public class TestDomainEvent : FlowOS.Events.Models.DomainEvent
    {
        public override string EventType { get; }
        public TestDomainEvent(Guid tenantId, string eventType) : base(tenantId, eventType)
        {
            EventType = eventType;
        }
    }
}
