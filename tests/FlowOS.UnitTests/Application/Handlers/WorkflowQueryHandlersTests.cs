using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.Handlers;
using FlowOS.Application.Queries;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Application.Handlers;

public class WorkflowQueryHandlersTests : IDisposable
{
    private readonly FlowOSDbContext _context;
    private readonly WorkflowQueryHandlers _handler;

    public WorkflowQueryHandlersTests()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
            
        _context = new FlowOSDbContext(options);
        IUnitOfWork unitOfWork = new UnitOfWork(_context);
        _handler = new WorkflowQueryHandlers(unitOfWork);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task Handle_GetWorkflowsQuery_ShouldReturnMappedInstances()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        
        // Use reflection or a test builder to create a WorkflowClass since constructor requires arguments
        var blueprint = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint 
        { 
            Workflow = new FlowOS.Domain.Blueprints.WorkflowBlueprint { StartStepId = "Start" } 
        };
        var wc = new WorkflowClass(tenantId, "TestClass", "1.0", blueprint);
        
        var instance1 = new WorkflowInstance(tenantId, definitionId, wc.Id, 1, "Start");
        var instance2 = new WorkflowInstance(tenantId, definitionId, Guid.Empty, 1, "End"); // No class attached

        _context.WorkflowClasses.Add(wc);
        _context.WorkflowInstances.AddRange(instance1, instance2);
        await _context.SaveChangesAsync();

        var query = new GetWorkflowsQuery { TenantId = tenantId };

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        
        // Ensure left join works correctly
        var dto1 = result.FirstOrDefault(x => x.Id == instance1.Id);
        Assert.NotNull(dto1);
        Assert.Equal("TestClass", dto1.WorkflowClassName);
        
        var dto2 = result.FirstOrDefault(x => x.Id == instance2.Id);
        Assert.NotNull(dto2);
        Assert.Equal("Unknown", dto2.WorkflowClassName);
    }

    [Fact]
    public async Task Handle_GetWorkflowByIdQuery_ShouldReturnCorrectInstance()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var instance = new WorkflowInstance(tenantId, Guid.NewGuid(), Guid.Empty, 1, "Start");
        
        _context.WorkflowInstances.Add(instance);
        await _context.SaveChangesAsync();

        var query = new GetWorkflowByIdQuery 
        { 
            TenantId = tenantId,
            Id = instance.Id
        };

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(instance.Id, result.Id);
        Assert.Equal("Unknown", result.WorkflowClassName); // Left join should fall back to Unknown
    }
    
    [Fact]
    public async Task Handle_GetWorkflowByIdQuery_WithInvalidId_ShouldReturnNull()
    {
        // Arrange
        var query = new GetWorkflowByIdQuery 
        { 
            TenantId = Guid.NewGuid(),
            Id = Guid.NewGuid()
        };

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }
}
