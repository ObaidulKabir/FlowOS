using System;
using System.Threading.Tasks;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FlowOS.UnitTests.Events;

public class EventRegistryTests
{
    private readonly FlowOSDbContext _context;
    private readonly IEventRegistry _registry;

    public EventRegistryTests()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _context = new FlowOSDbContext(options);
        var logger = new Mock<ILogger<EventRegistry>>();
        _registry = new EventRegistry(_context, logger.Object);
    }

    [Fact]
    public async Task ValidateAsync_ShouldSucceed_WhenEventExists()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var evt = new EventDefinition("EVT-TEST-01", tenantId, "Test", "Desc", "Test", EventCategory.System);
        _context.EventDefinitions.Add(evt);
        await _context.SaveChangesAsync();

        // Act & Assert
        await _registry.ValidateAsync("EVT-TEST-01", tenantId);
        // No exception means success
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrow_WhenEventDoesNotExist()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => 
            _registry.ValidateAsync("EVT-UNKNOWN", tenantId));
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrow_WhenEventExistsInDifferentTenant()
    {
        // Arrange
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var evt = new EventDefinition("EVT-TEST-01", tenant1, "Test", "Desc", "Test", EventCategory.System);
        _context.EventDefinitions.Add(evt);
        await _context.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => 
            _registry.ValidateAsync("EVT-TEST-01", tenant2));
    }
}
