using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FlowOS.Application.Commands;
using FlowOS.Core.Security;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using FlowOS.UnitTests.Workflows;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.UnitTests.Integration;

public class SandboxSampleWorkflowTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly CustomWebApplicationFactory<Program> _factory;

    public SandboxSampleWorkflowTests(CustomWebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DemoApiKey_StartsSampleWorkflow_WhenMockAuthIsOff()
    {
        var client = CreateSandboxClient(out _);

        client.DefaultRequestHeaders.Add("X-Mock-Role", "Tenant");
        client.DefaultRequestHeaders.Add("X-API-Key", "flowos_prod_secret_key_32_chars_min");
        client.DefaultRequestHeaders.Add("x-tenant-id", TenantIdentityRules.DemoTenantId.ToString());

        var response = await client.PostAsJsonAsync("/api/workflows/start", new StartWorkflowCommand(
            TenantIdentityRules.DemoTenantId,
            null,
            "ExpenseApprovalV2",
            null,
            Guid.Empty,
            null,
            Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StartResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.WorkflowInstanceId);
    }

    [Fact]
    public async Task DemoApiKey_RejectsTenantHeaderMismatch()
    {
        var client = CreateSandboxClient(out _);
        client.DefaultRequestHeaders.Add("X-API-Key", "flowos_prod_secret_key_32_chars_min");
        client.DefaultRequestHeaders.Add("x-tenant-id", Guid.Parse("11111111-1111-1111-1111-111111111111").ToString());

        var response = await client.PostAsJsonAsync("/api/workflows/start", new StartWorkflowCommand(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            null,
            "ExpenseApprovalV2"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateSandboxClient(out Guid definitionId)
    {
        var dbName = "FlowOS_SandboxSample_" + Guid.NewGuid();
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FlowOS:Identity:AllowMockAuth"] = "false",
                    ["FLOWOS_BILLING_ENFORCE"] = "true",
                    ["UseInMemoryDatabase"] = "true",
                    ["ConnectionStrings:DefaultConnection"] = ""
                });
            });
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);

                services.AddScoped<FlowOSDbContext>(_ =>
                {
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase(dbName)
                        .Options;
                    return new TestFlowOSDbContext(options);
                });
            });
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var demoTenantId = TenantIdentityRules.DemoTenantId;

        var tenant = db.Tenants.FirstOrDefault(t => t.TenantId == demoTenantId);
        if (tenant == null)
        {
            tenant = new Tenant("Demo Client Tenant");
            typeof(Tenant).GetProperty(nameof(Tenant.TenantId), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(tenant, demoTenantId);
            db.Tenants.Add(tenant);
        }
        if (!tenant.CanRunRuntime)
            tenant.AssignPlan(TenantPlan.Managed, TenantBillingStatus.Active);

        var admin = db.Roles.FirstOrDefault(r => r.TenantId == demoTenantId && r.Name == "Admin");
        if (admin == null)
        {
            admin = new Role(demoTenantId, "Admin");
            db.Roles.Add(admin);
        }
        admin.AddPermission("workflow.start");
        admin.AddPermission("workflow.read");
        admin.AddPermission("event.publish");
        admin.AddPermission("task.complete");

        if (!db.WorkflowDefinitions.Any(d => d.TenantId == demoTenantId && d.Name == "ExpenseApprovalV2"))
        {
            var blueprint = new WorkflowClassBlueprint
            {
                Events = new()
                {
                    new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request" }
                },
                StateMachine = new StateMachineBlueprint
                {
                    InitialState = "Draft",
                    States = new() { "Draft", "PendingManager" },
                    Transitions = new()
                    {
                        new TransitionBlueprint { FromState = "Draft", ToState = "PendingManager", EventId = "EVT-SUBMIT" }
                    }
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Draft",
                    Steps = new()
                    {
                        new StepBlueprint
                        {
                            StepId = "Draft",
                            StepType = "Command",
                            NextSteps = new Dictionary<string, string> { { "EVT-SUBMIT", "PendingManager" } }
                        },
                        new StepBlueprint
                        {
                            StepId = "PendingManager",
                            StepType = "HumanTask",
                            NextSteps = new Dictionary<string, string> { { "EVT-APPROVE", "PendingManager" } }
                        }
                    }
                }
            };

            if (!db.WorkflowClasses.Any(c => c.TenantId == demoTenantId && c.Name == "ExpenseApprovalV2"))
            {
                var workflowClass = new WorkflowClass(demoTenantId, "ExpenseApprovalV2", "1.0.0", blueprint);
                new WorkflowClassManager().Publish(workflowClass);
                db.WorkflowClasses.Add(workflowClass);
            }

            var definition = new WorkflowDefinition(demoTenantId, "ExpenseApprovalV2", 1, "Draft");
            var draft = new WorkflowStepDefinition("Draft", WorkflowStepType.Command);
            draft.NextSteps.Add("EVT-SUBMIT", "PendingManager");
            definition.AddStep(draft);
            var pending = new WorkflowStepDefinition("PendingManager", WorkflowStepType.HumanTask);
            pending.NextSteps.Add("EVT-APPROVE", "PendingManager");
            definition.AddStep(pending);
            definition.Publish();
            db.WorkflowDefinitions.Add(definition);
        }

        db.SaveChanges();
        definitionId = db.WorkflowDefinitions
            .Where(d => d.TenantId == demoTenantId && d.Name == "ExpenseApprovalV2")
            .Select(d => d.Id)
            .First();

        return factory.CreateClient();
    }

    private sealed class StartResponse
    {
        public Guid WorkflowInstanceId { get; set; }
    }
}
