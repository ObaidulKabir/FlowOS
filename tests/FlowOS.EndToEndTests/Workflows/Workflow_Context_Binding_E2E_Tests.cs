using System.Net.Http.Json;
using System.Text.Json;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.EndToEndTests.Workflows;

public class Workflow_Context_Binding_E2E_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public Workflow_Context_Binding_E2E_Tests(WebApplicationFactory<Program> factory)
    {
        var databaseName = "FlowOS_ContextBinding_E2E_" + Guid.NewGuid();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(x =>
                             x.ServiceType == typeof(FlowOSDbContext) ||
                             x.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList())
                {
                    services.Remove(descriptor);
                }

                services.AddScoped<FlowOSDbContext>(_ =>
                {
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase(databaseName)
                        .Options;
                    return new TestFlowOSDbContext(options);
                });
            });
        });
    }

    [Fact]
    public async Task Template_Can_Be_Bound_Activated_Started_And_Advanced_By_Context()
    {
        Guid sourceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var source = CreateTemplate();
            var publication = new WorkflowClassManager().Publish(source);
            Assert.True(publication.IsValid);
            db.WorkflowClasses.Add(source);

            var admin = new Role(_tenantId, "Admin");
            admin.AddPermission("event.publish");
            admin.AddPermission("workflow.start");
            db.Roles.Add(admin);
            var financeManager = new Role(_tenantId, "FinanceManager");
            financeManager.AddPermission("event.publish.EVT-APPROVE");
            financeManager.AddPermission("event.publish.EVT-EXP-APPROVE");
            db.Roles.Add(financeManager);
            var hrManager = new Role(_tenantId, "HRManager");
            hrManager.AddPermission("event.publish.EVT-APPROVE");
            db.Roles.Add(hrManager);
            await db.SaveChangesAsync();
            sourceId = source.Id;
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
        client.DefaultRequestHeaders.Add("X-Mock-UserId", "context-e2e");

        var createResponse = await client.PostAsJsonAsync("/api/context-bindings", new
        {
            sourceWorkflowClassId = sourceId,
            contextType = "Expense",
            name = "ExpenseApproval",
            definition = new
            {
                entityType = "ExpenseEntity",
                eventAliases = new Dictionary<string, string>
                {
                    ["EVT-APPROVE"] = "EVT-EXP-APPROVE"
                },
                roleOverrides = new Dictionary<string, string>
                {
                    ["Approver"] = "FinanceManager"
                },
                inputMapping = new Dictionary<string, string>
                {
                    ["Amount"] = "expense.amount"
                },
                conditionParameters = new Dictionary<string, object>
                {
                    ["ApprovalLimit"] = 5000
                }
            }
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var bindingId = created.GetProperty("id").GetGuid();

        var draftSimulationResponse = await client.PostAsJsonAsync("/api/context-bindings/simulate", new
        {
            contextBindingId = bindingId,
            revision = "draft",
            initialPayload = new { expense = new { amount = 1250 } },
            roles = new[] { "FinanceManager" },
            events = new[] { new { eventType = "EVT-EXP-APPROVE" } }
        });
        draftSimulationResponse.EnsureSuccessStatusCode();
        var draftSimulation = await draftSimulationResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", draftSimulation.GetProperty("status").GetString());
        Assert.False(draftSimulation.GetProperty("isPersistedRuntime").GetBoolean());
        Assert.True(draftSimulation.GetProperty("sideEffectsSuppressed").GetBoolean());
        Assert.Equal(
            "EVT-APPROVE",
            draftSimulation.GetProperty("trace")[1].GetProperty("canonicalEventType").GetString());

        var roleDeniedSimulationResponse = await client.PostAsJsonAsync("/api/context-bindings/simulate", new
        {
            contextBindingId = bindingId,
            revision = "draft",
            initialPayload = new { expense = new { amount = 1250 } },
            events = new[] { new { eventType = "EVT-EXP-APPROVE" } }
        });
        roleDeniedSimulationResponse.EnsureSuccessStatusCode();
        var roleDeniedSimulation = await roleDeniedSimulationResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Denied", roleDeniedSimulation.GetProperty("status").GetString());
        Assert.Contains(
            "FinanceManager",
            roleDeniedSimulation.GetProperty("trace")[1].GetProperty("reason").GetString());

        var validateResponse = await client.PostAsync($"/api/context-bindings/{bindingId}/validate", null);
        validateResponse.EnsureSuccessStatusCode();
        var validation = await validateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(validation.GetProperty("isValid").GetBoolean());

        var activateResponse = await client.PostAsync($"/api/context-bindings/{bindingId}/activate", null);
        activateResponse.EnsureSuccessStatusCode();

        var activeSimulationResponse = await client.PostAsJsonAsync("/api/context-bindings/simulate", new
        {
            contextBindingId = bindingId,
            revision = "active",
            initialPayload = new { expense = new { amount = 1250 } },
            roles = new[] { "FinanceManager" },
            events = new[] { new { eventType = "EVT-EXP-APPROVE" } }
        });
        activeSimulationResponse.EnsureSuccessStatusCode();
        var activeSimulation = await activeSimulationResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(activeSimulation.GetProperty("isPersistedRuntime").GetBoolean());
        Assert.Equal("active", activeSimulation.GetProperty("revisionKind").GetString());

        var createLeaveResponse = await client.PostAsJsonAsync("/api/context-bindings", new
        {
            sourceWorkflowClassId = sourceId,
            contextType = "Leave",
            name = "LeaveApproval",
            definition = new
            {
                entityType = "LeaveEntity",
                eventAliases = new Dictionary<string, string>
                {
                    ["EVT-APPROVE"] = "EVT-LEAVE-APPROVE"
                },
                roleOverrides = new Dictionary<string, string>
                {
                    ["Approver"] = "HRManager"
                },
                inputMapping = new Dictionary<string, string>
                {
                    ["Amount"] = "leave.days"
                },
                conditionParameters = new Dictionary<string, object>
                {
                    ["ApprovalLimit"] = 10
                }
            }
        });
        createLeaveResponse.EnsureSuccessStatusCode();
        var createdLeave = await createLeaveResponse.Content.ReadFromJsonAsync<JsonElement>();
        var leaveBindingId = createdLeave.GetProperty("id").GetGuid();
        (await client.PostAsync($"/api/context-bindings/{leaveBindingId}/validate", null))
            .EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/context-bindings/{leaveBindingId}/activate", null))
            .EnsureSuccessStatusCode();

        var startResponse = await client.PostAsJsonAsync("/api/workflows/start", new
        {
            tenantId = Guid.Empty,
            contextType = "Expense",
            payload = new { expense = new { amount = 1250 } },
            businessReference = new { sourceSystem = "ERP", externalEntityId = "EXP-1042" }
        });
        startResponse.EnsureSuccessStatusCode();
        var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var instanceId = started.GetProperty("workflowInstanceId").GetGuid();

        var deniedEventResponse = await client.PostAsJsonAsync("/api/events/publish", new
        {
            tenantId = Guid.Empty,
            workflowInstanceId = instanceId,
            eventType = "EVT-EXP-APPROVE",
            payload = new { expense = new { amount = 6000 } }
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, deniedEventResponse.StatusCode);

        using (var rollbackScope = _factory.Services.CreateScope())
        {
            var rollbackDb = rollbackScope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var unchangedSnapshot = await rollbackDb.WorkflowContextSnapshots
                .AsNoTracking()
                .SingleAsync(x => x.WorkflowInstanceId == instanceId);
            Assert.Equal(1250, unchangedSnapshot.CanonicalData["Amount"].GetInt32());
        }

        var leaveStartResponse = await client.PostAsJsonAsync("/api/workflows/start", new
        {
            tenantId = Guid.Empty,
            contextType = "Leave",
            payload = new { leave = new { days = 15 } }
        });
        leaveStartResponse.EnsureSuccessStatusCode();
        var leaveStarted = await leaveStartResponse.Content.ReadFromJsonAsync<JsonElement>();
        var leaveInstanceId = leaveStarted.GetProperty("workflowInstanceId").GetGuid();
        var leaveEventResponse = await client.PostAsJsonAsync("/api/events/publish", new
        {
            tenantId = Guid.Empty,
            workflowInstanceId = leaveInstanceId,
            eventType = "EVT-LEAVE-APPROVE"
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, leaveEventResponse.StatusCode);

        var updateResponse = await client.PutAsJsonAsync($"/api/context-bindings/{bindingId}/draft", new
        {
            sourceWorkflowClassId = sourceId,
            definition = new
            {
                entityType = "ExpenseEntity",
                eventAliases = new Dictionary<string, string>
                {
                    ["EVT-APPROVE"] = "EVT-EXP-APPROVE"
                },
                roleOverrides = new Dictionary<string, string>
                {
                    ["Approver"] = "FinanceManager"
                },
                inputMapping = new Dictionary<string, string>
                {
                    ["Amount"] = "expense.amount"
                },
                conditionParameters = new Dictionary<string, object>
                {
                    ["ApprovalLimit"] = 1000
                }
            }
        });
        updateResponse.EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/context-bindings/{bindingId}/validate", null))
            .EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/context-bindings/{bindingId}/activate", null))
            .EnsureSuccessStatusCode();

        var newStartResponse = await client.PostAsJsonAsync("/api/workflows/start", new
        {
            tenantId = Guid.Empty,
            contextBindingId = bindingId,
            payload = new { expense = new { amount = 1250 } }
        });
        newStartResponse.EnsureSuccessStatusCode();
        var newStarted = await newStartResponse.Content.ReadFromJsonAsync<JsonElement>();
        var newInstanceId = newStarted.GetProperty("workflowInstanceId").GetGuid();

        var oldRevisionEventResponse = await client.PostAsJsonAsync("/api/events/publish", new
        {
            tenantId = Guid.Empty,
            workflowInstanceId = instanceId,
            eventType = "EVT-EXP-APPROVE"
        });
        oldRevisionEventResponse.EnsureSuccessStatusCode();

        var newRevisionEventResponse = await client.PostAsJsonAsync("/api/events/publish", new
        {
            tenantId = Guid.Empty,
            workflowInstanceId = newInstanceId,
            eventType = "EVT-EXP-APPROVE"
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, newRevisionEventResponse.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var instance = await verifyDb.WorkflowInstances.SingleAsync(x => x.Id == instanceId);
        var newInstance = await verifyDb.WorkflowInstances.SingleAsync(x => x.Id == newInstanceId);
        var snapshot = await verifyDb.WorkflowContextSnapshots.SingleAsync(x => x.WorkflowInstanceId == instanceId);
        var definition = await verifyDb.WorkflowDefinitions.SingleAsync(x => x.Id == instance.WorkflowDefinitionId);
        var newDefinition = await verifyDb.WorkflowDefinitions.SingleAsync(x => x.Id == newInstance.WorkflowDefinitionId);
        var revisions = await verifyDb.WorkflowContextBindingRevisions
            .Where(x => x.BindingId == bindingId)
            .OrderBy(x => x.Revision)
            .ToListAsync();

        Assert.NotNull(definition.ContextBindingRevisionId);
        Assert.NotNull(definition.StateMachineDefinitionId);
        Assert.NotEqual(instance.WorkflowDefinitionId, newInstance.WorkflowDefinitionId);
        Assert.Equal(1, definition.Version);
        Assert.Equal(2, newDefinition.Version);
        Assert.Equal(2, revisions.Count);
        Assert.Equal(FlowOS.Domain.Enums.WorkflowContextBindingRevisionStatus.Superseded, revisions[0].Status);
        Assert.Equal(FlowOS.Domain.Enums.WorkflowContextBindingRevisionStatus.Active, revisions[1].Status);
        Assert.Equal(1250, snapshot.CanonicalData["Amount"].GetInt32());
        Assert.Equal(5000, snapshot.CanonicalData["ApprovalLimit"].GetInt32());
        Assert.Equal("ERP", snapshot.SourceSystem);

        var timeTravel = verifyScope.ServiceProvider
            .GetRequiredService<FlowOS.Core.Common.Interfaces.IWorkflowTimeTravelService>();
        var replay = await timeTravel.GetReplayTimelineAsync(_tenantId, instanceId);
        Assert.NotNull(replay);
        Assert.Equal(1250L, replay!.Snapshots.First().Variables["Amount"]);
        Assert.Equal(5000L, replay.Snapshots.First().Variables["ApprovalLimit"]);
        Assert.Equal(bindingId, replay.ContextBindingId);
        Assert.Equal("Expense", replay.ContextType);
        Assert.Equal("ERP", replay.SourceSystem);
        Assert.Equal("WorkflowStarted", replay.Snapshots.First().CanonicalEventType);

        var instanceCountBeforeFork = await verifyDb.WorkflowInstances.CountAsync();
        var eventCountBeforeFork = await verifyDb.Events.CountAsync();
        var fork = await timeTravel.SimulateForkAsync(
            _tenantId,
            instanceId,
            0,
            "EVT-EXP-APPROVE",
            simulatedRoles: ["FinanceManager"]);
        Assert.True(fork.IsAllowed, fork.Reason);
        Assert.Equal("EVT-APPROVE", fork.CanonicalEventType);
        Assert.Equal(bindingId, fork.ContextBindingId);
        Assert.Contains("FinanceManager", fork.SimulatedRoles!);
        Assert.Equal(1250L, fork.ProjectedCanonicalContext!["Amount"]);
        Assert.Equal(instanceCountBeforeFork, await verifyDb.WorkflowInstances.CountAsync());
        Assert.Equal(eventCountBeforeFork, await verifyDb.Events.CountAsync());
    }

    [Fact]
    public async Task ContextBinding_Id_From_Another_Tenant_Returns_NotFound()
    {
        Guid sourceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var source = CreateTemplate();
            Assert.True(new WorkflowClassManager().Publish(source).IsValid);
            db.WorkflowClasses.Add(source);
            await db.SaveChangesAsync();
            sourceId = source.Id;
        }

        var ownerClient = _factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        ownerClient.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
        ownerClient.DefaultRequestHeaders.Add("X-Mock-UserId", "binding-owner");
        var createResponse = await ownerClient.PostAsJsonAsync("/api/context-bindings", new
        {
            sourceWorkflowClassId = sourceId,
            contextType = "Expense",
            name = "ExpenseApproval",
            definition = new { entityType = "ExpenseEntity" }
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var bindingId = created.GetProperty("id").GetGuid();

        var otherTenantClient = _factory.CreateClient();
        otherTenantClient.DefaultRequestHeaders.Add("x-tenant-id", Guid.NewGuid().ToString());
        otherTenantClient.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
        otherTenantClient.DefaultRequestHeaders.Add("X-Mock-UserId", "other-tenant");

        var response = await otherTenantClient.GetAsync($"/api/context-bindings/{bindingId}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        var simulationResponse = await otherTenantClient.PostAsJsonAsync("/api/context-bindings/simulate", new
        {
            contextBindingId = bindingId,
            revision = "draft",
            initialPayload = new { }
        });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, simulationResponse.StatusCode);
    }

    private WorkflowClass CreateTemplate()
        => new(
            _tenantId,
            "ReusableApproval",
            "1.0.0",
            new WorkflowClassBlueprint
            {
                ContextSchema = """{"type":"object","required":["Amount","ApprovalLimit"],"properties":{"Amount":{"type":"number"},"ApprovalLimit":{"type":"number"}}}""",
                Events =
                [
                    new EventBlueprint
                    {
                        EventId = "EVT-APPROVE",
                        Name = "Approve",
                        Category = EventCategory.Human,
                        RequiredCapabilities = ["event.publish.EVT-APPROVE"]
                    }
                ],
                StateMachine = new StateMachineBlueprint
                {
                    EntityType = "ApprovalSubject",
                    InitialState = "Draft",
                    States = ["Draft", "Approved"],
                    Transitions =
                    [
                        new TransitionBlueprint
                        {
                            FromState = "Draft",
                            ToState = "Approved",
                            EventId = "EVT-APPROVE",
                            Condition = "Amount <= ApprovalLimit"
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
                            RequiredCapabilities = ["event.publish.EVT-APPROVE"],
                            NextSteps = new Dictionary<string, string> { ["EVT-APPROVE"] = "END" }
                        }
                    ]
                },
                Roles =
                [
                    new RoleBlueprint
                    {
                        Name = "Approver",
                        GrantedCapabilities = ["event.publish.EVT-APPROVE"]
                    }
                ],
                Capabilities = [new CapabilityBlueprint { Code = "event.publish.EVT-APPROVE" }]
            });
}
