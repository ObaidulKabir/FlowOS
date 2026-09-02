using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Events.Abstractions;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.BackgroundServices;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class InMemoryCleanupTests
{
    [Fact]
    public async Task InMemoryDataCleanupService_PurgesDataOlderThanTtlHours()
    {
        var services = new ServiceCollection();
        
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        services.AddScoped(_ => new FlowOSDbContext(options));

        var serviceProvider = services.BuildServiceProvider();

        // Seed data: 1 old instance (5 hours old), 1 recent instance (1 hour old)
        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            var tenantId = Guid.NewGuid();
            var defId = Guid.NewGuid();
            var classId = Guid.NewGuid();

            var oldInstance = new WorkflowInstance(tenantId, defId, classId, 1, "Start");
            typeof(WorkflowInstance).GetField($"<{nameof(WorkflowInstance.CreatedAt)}>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(oldInstance, DateTime.UtcNow.AddHours(-5));

            var recentInstance = new WorkflowInstance(tenantId, defId, classId, 1, "Start");

            db.WorkflowInstances.AddRange(oldInstance, recentInstance);

            var oldEvent = new StandardEvent(tenantId, "EVT-TEST");
            typeof(FlowOS.Events.Models.DomainEvent).GetField($"<{nameof(StandardEvent.Timestamp)}>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(oldEvent, DateTime.UtcNow.AddHours(-5));

            var recentEvent = new StandardEvent(tenantId, "EVT-RECENT");

            db.Events.AddRange(oldEvent, recentEvent);

            await db.SaveChangesAsync();
        }

        // Configure 4 hour TTL
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["UseInMemoryDatabase"] = "true",
            ["Sandbox:DataTtlHours"] = "4",
            ["Sandbox:AutoCleanupEnabled"] = "true"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var cleanupService = new InMemoryDataCleanupService(
            serviceProvider,
            config,
            NullLogger<InMemoryDataCleanupService>.Instance);

        // Run one purge pass via reflection or calling private method
        var purgeMethod = typeof(InMemoryDataCleanupService)
            .GetMethod("PurgeExpiredInMemoryDataAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        await (Task)purgeMethod!.Invoke(cleanupService, new object[] { CancellationToken.None })!;

        // Verify old records removed, recent records retained
        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var instances = await db.WorkflowInstances.ToListAsync();
            var events = await db.Events.ToListAsync();

            Assert.Single(instances);
            Assert.Single(events);
            Assert.Equal("EVT-RECENT", events.First().EventType);
        }
    }
}
