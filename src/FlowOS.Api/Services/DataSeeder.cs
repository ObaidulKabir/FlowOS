using System;
using System.Reflection;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration; // Added
using Microsoft.Extensions.DependencyInjection;
using FlowOS.Security.Models; // Ensure this is present
using FlowOS.Domain.Enums; // Ensure this is present for WorkflowClassStatus
using FlowOS.Domain.Services; // For WorkflowClassManager
using FlowOS.Events.Models; // For StandardEvent
using FlowOS.Application.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.ValueObjects;
using System.Text.Json;

namespace FlowOS.API.Services;

public static class DataSeeder
{
    public static readonly Guid DefaultTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static async Task SeedAsync(FlowOSDbContext context, IServiceProvider serviceProvider, IHostEnvironment env)
    {
        // 1. Ensure Tenant
        if (!await context.Tenants.AnyAsync(t => t.TenantId == DefaultTenantId))
        {
            var tenant = new Tenant("Default Tenant");
            SetPrivateProperty(tenant, "TenantId", DefaultTenantId);
            tenant.AssignPlan(TenantPlan.Managed, TenantBillingStatus.Active);
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        var demoClientTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        if (!await context.Tenants.AnyAsync(t => t.TenantId == demoClientTenantId))
        {
            var clientTenant = new Tenant("Demo Client Tenant");
            SetPrivateProperty(clientTenant, "TenantId", demoClientTenantId);
            clientTenant.AssignPlan(TenantPlan.Managed, TenantBillingStatus.Active);
            context.Tenants.Add(clientTenant);
            await context.SaveChangesAsync();
        }
        else
        {
            var existingDemo = await context.Tenants.FirstAsync(t => t.TenantId == demoClientTenantId);
            if (!existingDemo.CanRunRuntime)
            {
                existingDemo.AssignPlan(TenantPlan.Managed, TenantBillingStatus.Active);
                await context.SaveChangesAsync();
            }
        }

        // 1.2 Ensure Demo Tenant API Keys
        if (!await context.TenantApiKeys.AnyAsync(k => k.TenantId == demoClientTenantId))
        {
            var demoKey1 = new TenantApiKey(demoClientTenantId, "Production Key", "flowos_prod_secret_key_32_chars_min", "ERP Integration", "Production", new[] { "*" });
            var demoKey2 = new TenantApiKey(demoClientTenantId, "Local Dev Key", "local-development-key-change-me", "Developer Sandbox", "Development", new[] { "*" });
            context.TenantApiKeys.AddRange(demoKey1, demoKey2);
            await context.SaveChangesAsync();
        }

        // 1.3 Ensure Demo Tenant User
        if (!await context.TenantUsers.AnyAsync(u => u.TenantId == demoClientTenantId))
        {
            var passwordHasher = serviceProvider.GetService<FlowOS.Security.Interfaces.IPasswordHasher>()
                ?? new FlowOS.Infrastructure.Services.Security.Pbkdf2PasswordHasher();
            var demoUser = new TenantUser(
                demoClientTenantId,
                "demo@flowos.internal",
                passwordHasher.HashPassword("demo-password-123"),
                "Demo Administrator",
                "TenantAdmin",
                isEmailVerified: true);
            context.TenantUsers.Add(demoUser);
            await context.SaveChangesAsync();
        }


        // 1.5 Ensure Admin Role
        if (!await context.Roles.AnyAsync(r => r.Name == "Admin" && r.TenantId == DefaultTenantId))
        {
            var adminRole = new Role(DefaultTenantId, "Admin");
            adminRole.AddPermission("workflow.start");
            adminRole.AddPermission("workflow.create");
            adminRole.AddPermission("workflow.read");
            adminRole.AddPermission("event.publish");
            adminRole.AddPermission("task.complete");
            adminRole.AddPermission("workflow.approve_public");
            adminRole.AddPermission("role.create"); // Just in case
            adminRole.AddPermission("agent.insight.publish"); // For notifications
            
            context.Roles.Add(adminRole);
            await context.SaveChangesAsync();
        }

        // 2. Load Configuration (Dev Only)
        if (env.IsDevelopment())
        {
            var config = serviceProvider.GetService<IConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILogger<ConfigurationLoader>>();
            
            string configRoot = null;
            
            // Priority 1: User-Specified Working Directory
            var userWd = config?["FlowOS:WorkingDirectory"];
            if (!string.IsNullOrWhiteSpace(userWd))
            {
                try 
                {
                    WorkingDirectoryValidator.Validate(userWd);
                    configRoot = Path.Combine(userWd, "flowos-config");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "User-specified working directory is invalid.");
                    throw; // Fail fast if user explicitly provided a bad path
                }
            }
            
            // Priority 2: Fallback to Current Directory (Root of Repo in Dev)
            if (configRoot == null)
            {
                // Locate config folder relative to execution
                var potentialPaths = new[] 
                {
                    Path.Combine(Directory.GetCurrentDirectory(), "flowos-config"), // If running from root
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "flowos-config") // If running from src/FlowOS.API
                };
                
                foreach (var path in potentialPaths)
                {
                    if (Directory.Exists(path))
                    {
                        // Validate the parent directory of config is a valid project root
                        var projectRoot = Path.GetDirectoryName(path);
                        try
                        {
                            WorkingDirectoryValidator.Validate(projectRoot!);
                            configRoot = path;
                            break;
                        }
                        catch (Exception) { /* Skip invalid candidates */ }
                    }
                }
            }

            if (configRoot != null && Directory.Exists(configRoot))
            {
                // Pass the PROJECT ROOT, not the config folder, if ConfigurationLoader expects root?
                // Looking at ConfigurationLoader code:
                // var path = Path.Combine(_configRoot, "events");
                // So _configRoot should be the folder containing "events".
                // In DataSeeder, we set configRoot = .../flowos-config.
                // So that matches.
                
                // Note: ConfigurationLoader constructor now calls Validate(_configRoot).
                // But _configRoot is "flowos-config" folder.
                // WorkingDirectoryValidator checks for "bin/Debug". "flowos-config" is fine.
                
                var loader = new ConfigurationLoader(context, logger, configRoot);
                await loader.LoadAllAsync(DefaultTenantId);
            }
            else 
            {
                 logger.LogWarning("Could not find valid 'flowos-config' directory. Please set 'FlowOS:WorkingDirectory'.");
            }
        }

        // 3. Seed WorkflowClass for Default Client (E2E Demo)
        // Check if our demo client tenant has any WorkflowClasses, if not, create one.
        var clientTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222"); // Matches E2E tests / Dashboard default
        
        if (!await context.WorkflowClasses.AnyAsync(w => w.TenantId == clientTenantId))
        {
            var demoBp = CreateExpenseApprovalBlueprint();

            var manager = new WorkflowClassManager();

            var demoWc = new WorkflowClass(clientTenantId, "ExpenseApproval", "1.0.0", demoBp);
            manager.Publish(demoWc); // Create Definition
            context.WorkflowClasses.Add(demoWc);
            
            // Add a Public Template too
            var publicBp = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint { /* minimal valid */ };
             // Reuse valid logic
             var bpValid = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
            {
                Events = new() { new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-GO", Name = "Go" } },
                StateMachine = new FlowOS.Domain.Blueprints.StateMachineBlueprint 
                { 
                    InitialState = "Start", States = new() { "Start", "End" }, 
                    Transitions = new() { new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "Start", ToState = "End", EventId = "EVT-GO" } } 
                },
                Workflow = new FlowOS.Domain.Blueprints.WorkflowBlueprint 
                { 
                    StartStepId = "Start",
                    Steps = new() 
                { 
                        new FlowOS.Domain.Blueprints.StepBlueprint { StepId = "Start", StepType = "Command", NextSteps = new() { { "EVT-GO", "End" } } },
                        new FlowOS.Domain.Blueprints.StepBlueprint { StepId = "End", StepType = "Command", NextSteps = new() { { "Default", "END" } } }
                    } 
                }
            };
            
            var publicWc = new WorkflowClass(Guid.Empty, "GlobalTemplate", "1.0.0", bpValid);
            manager.Publish(publicWc);
            manager.SubmitForReview(publicWc);
            manager.ApproveAsPublic(publicWc);
            context.WorkflowClasses.Add(publicWc);

            await context.SaveChangesAsync();
        }

        // 3.5 FIX: Ensure ExpenseApproval Definition exists (because previous run might have skipped Publish)
        // Check if definition exists for the client tenant
        var defExists = await context.WorkflowDefinitions.AnyAsync(d => d.TenantId == clientTenantId && d.Name == "ExpenseApproval");
        Console.WriteLine($"[DataSeeder] defExists: {defExists}");

        if (!defExists)
        {
            Console.WriteLine("[DataSeeder] Definition does not exist. Creating...");
            var wc = await context.WorkflowClasses.FirstOrDefaultAsync(w => w.TenantId == clientTenantId && w.Name == "ExpenseApproval");
            if (wc != null)
            {
                Console.WriteLine($"[DataSeeder] Found WorkflowClass: {wc.Id}. Creating definition.");
                var demoBpFix = CreateExpenseApprovalBlueprint();
                
                SetPrivateProperty(wc, "Definition", demoBpFix);
                if (wc.Status != WorkflowClassStatus.Published)
                {
                    var manager = new WorkflowClassManager();
                    manager.Publish(wc);
                }
                
                var def = WorkflowClassCompiler.MapToRuntimeDefinition(wc);
                context.WorkflowDefinitions.Add(def);

                await context.SaveChangesAsync();
            }
        }
        else
        {
            Console.WriteLine("[DataSeeder] Definition already exists. Checking for updates...");
                // FORCE UPDATE EXISTING DEFINITION
                var existingDef = await context.WorkflowDefinitions
                    .Include(d => d.Steps)
                    .FirstOrDefaultAsync(d => d.TenantId == clientTenantId && d.Name == "ExpenseApproval");
                
                if (existingDef != null)
                {
                    Console.WriteLine($"[DataSeeder] Found existing definition: {existingDef.Id}. Steps: {existingDef.Steps.Count}");
                    
                    // Debug existing steps
                    foreach(var s in existingDef.Steps)
                    {
                         Console.WriteLine($"[DataSeeder] Step {s.StepId}: {string.Join(", ", s.NextSteps.Keys)}");
                    }

                    Console.WriteLine("[DataSeeder] Updating existing definition for ExpenseApproval...");
                    // Clear existing steps (EF Core will track deletion)
                    existingDef.Steps.Clear();
                    
                    var demoBpFix = CreateExpenseApprovalBlueprint();

                    foreach (var stepBp in demoBpFix.Workflow.Steps)
                    {
                        var stepType = Enum.Parse<WorkflowStepType>(stepBp.StepType);
                        var stepDef = new WorkflowStepDefinition(stepBp.StepId, stepType)
                        {
                            AllowedRoles = stepBp.RequiredRoles.ToList()
                        };
                        foreach (var next in stepBp.NextSteps)
                        {
                            stepDef.NextSteps.Add(next.Key, next.Value);
                        }
                        // Use public method AddStep but it throws if Published.
                        // So we add directly to Steps collection via reflection or if protected setter is accessible?
                        // WorkflowDefinition.Steps is public List<WorkflowStepDefinition> { get; private set; }
                        // But it's initialized in constructor.
                        // Wait, AddStep throws if Status != Draft.
                        // existingDef.Status is likely Published.
                        // So I need to set status to Draft temporarily via reflection.
                        SetPrivateProperty(existingDef, "Status", WorkflowStatus.Draft);
                        existingDef.AddStep(stepDef);
                    }

                    var existingClass = await context.WorkflowClasses.FirstOrDefaultAsync(
                        w => w.TenantId == clientTenantId && w.Name == "ExpenseApproval");
                    if (existingClass != null)
                        SetPrivateProperty(existingClass, "Definition", demoBpFix);

                    WorkflowClassCompiler.ApplyTemplateBusinessRoles(existingDef, demoBpFix);
                    SetPrivateProperty(existingDef, "Status", WorkflowStatus.Published);
                    await context.SaveChangesAsync();
                }
            }
        
        // Ensure EventDefinitions exist (Always run this check)
        var events = new[] { "EVT-SUBMIT", "EVT-APPROVE", "EVT-REJECT", "EVT-ESCALATE", "EVT-DIRECTOR-APPROVE", "EVT-DIRECTOR-REJECT" };
        foreach (var evtId in events)
        {
            var exists = await context.EventDefinitions.AnyAsync(e => e.EventId == evtId && e.TenantId == clientTenantId);
            if (!exists)
            {
                Console.WriteLine($"[DataSeeder] Adding Event: {evtId}");
                var evtDef = new EventDefinition(evtId, clientTenantId, evtId, "Seeded Event", "Expense", FlowOS.Domain.Enums.EventCategory.Human, 1);
                evtDef.Publish();
                context.EventDefinitions.Add(evtDef);
            }
            else
            {
                Console.WriteLine($"[DataSeeder] Event {evtId} already exists.");
            }
        }
        await context.SaveChangesAsync();

    // 4. Seed ExpenseApproval v2 (Conditional Logic)
    var v2Name = "ExpenseApprovalV2";
    if (!await context.WorkflowClasses.AnyAsync(w => w.TenantId == clientTenantId && w.Name == v2Name))
    {
        Console.WriteLine($"[DataSeeder] Creating {v2Name}...");
        var v2Bp = CreateExpenseApprovalV2Blueprint();
        var v2Wc = new WorkflowClass(clientTenantId, v2Name, "1.0.0", v2Bp);
        SetPrivateProperty(v2Wc, "Id", Guid.Parse("e912ab44-2222-2222-2222-222222222222"));
        var manager2 = new WorkflowClassManager();
        manager2.Publish(v2Wc);
        context.WorkflowClasses.Add(v2Wc);
        
        var def = WorkflowClassCompiler.MapToRuntimeDefinition(v2Wc);
        context.WorkflowDefinitions.Add(def);
        await context.SaveChangesAsync();
    }

    await RepairExpenseApprovalV2GraphAsync(context, clientTenantId);
    
    // 5. Ensure runtime roles for sandbox + production API keys (demo key maps to Admin).
    await EnsureRoleWithPermissionsAsync(
        context,
        clientTenantId,
        "Admin",
        "workflow.start", "workflow.create", "workflow.read", "event.publish", "task.complete", "workflow.approve_public");
    await EnsureRoleWithPermissionsAsync(
        context,
        clientTenantId,
        "Tenant",
        "workflow.start", "workflow.create", "workflow.read", "event.publish", "task.complete");
    await EnsureRoleWithPermissionsAsync(
        context,
        clientTenantId,
        "ApiKey",
        "workflow.start", "workflow.create", "workflow.read", "event.publish", "task.complete");
    
    await EnsureRoleWithPermissionsAsync(
        context,
        clientTenantId,
        "User",
        "workflow.start", "workflow.read",
        "event.publish.EVT-SUBMIT", "event.publish.EVT-APPLY", "event.publish.EVT-REQUEST-ACCESS");
    await EnsureRoleWithPermissionsAsync(
        context,
        clientTenantId,
        "Employee",
        "workflow.start", "workflow.read",
        "event.publish.EVT-SUBMIT", "event.publish.EVT-APPLY", "event.publish.EVT-REQUEST-ACCESS");
    await EnsureRoleWithPermissionsAsync(
        context,
        clientTenantId,
        "Manager",
        "workflow.read",
        "event.publish.EVT-APPROVE", "event.publish.EVT-REJECT", "event.publish.EVT-ESCALATE",
        "event.publish.EVT-FINAL-APPROVE", "event.publish.EVT-DECLINE");
    await EnsureRoleWithPermissionsAsync(
        context,
        clientTenantId,
        "Director",
        "workflow.read",
        "event.publish.EVT-DIRECTOR-APPROVE", "event.publish.EVT-DIRECTOR-REJECT",
        "event.publish.EVT-APPROVE", "event.publish.EVT-REJECT",
        "event.publish.EVT-FINAL-APPROVE", "event.publish.EVT-DECLINE");
    
    await context.SaveChangesAsync();

        // 6. Ensure at least one live instance exists for Tenant / Admin view (Production only)
        if (env.IsProduction() && !await context.WorkflowInstances.AnyAsync(w => w.TenantId == clientTenantId))
        {
            var defV2 = await context.WorkflowDefinitions.FirstOrDefaultAsync(d => d.TenantId == clientTenantId && d.Name == v2Name);
            var wcV2 = await context.WorkflowClasses.FirstOrDefaultAsync(w => w.TenantId == clientTenantId && w.Name == v2Name);
            if (defV2 != null && wcV2 != null)
            {
                var demoInstance = new WorkflowInstance(
                    clientTenantId,
                    defV2.Id,
                    wcV2.Id,
                    1,
                    "PendingManager",
                    Guid.NewGuid()
                );
                SetPrivateProperty(demoInstance, "CurrentState", "PendingManager");
                context.WorkflowInstances.Add(demoInstance);
                await context.SaveChangesAsync();
            }
        }

        // 7. Seed Enterprise Flagship Workflows
        await SeedFlagshipWorkflowsAsync(context, clientTenantId);
        await EnsurePublishedRuntimeDefinitionsAsync(context, clientTenantId);
        await RepairSandboxSampleDeclarationsAsync(context, clientTenantId);
        await SeedSampleBusinessContextsAsync(context, clientTenantId);
    }

    public static async Task SeedFlagshipWorkflowsAsync(FlowOSDbContext context, Guid clientTenantId)
    {
        var manager = new WorkflowClassManager();

        // -------------------------------------------------------------------------------------------------
        // FLAGSHIP 1: OrderSagaFulfillment (Distributed Sagas & OnFailure Rollback Compensations)
        // -------------------------------------------------------------------------------------------------
        const string sagaName = "OrderSagaFulfillment";
        if (!await context.WorkflowClasses.AnyAsync(w => w.TenantId == clientTenantId && w.Name == sagaName))
        {
            var sagaBp = new WorkflowClassBlueprint
            {
                Events = new()
                {
                    new() { EventId = "EVT-VALIDATE", Name = "Validate Order" },
                    new() { EventId = "EVT-PAY-SUCCESS", Name = "Payment Authorized" },
                    new() { EventId = "EVT-STOCK-LOCKED", Name = "Inventory Reserved" },
                    new() { EventId = "EVT-SHIP-FAIL", Name = "Shipping Generation Failed" },
                    new() { EventId = "EVT-COMPENSATE", Name = "Rollback Completed" }
                },
                StateMachine = new StateMachineBlueprint
                {
                    InitialState = "Draft",
                    States = new() { "Draft", "PaymentAuthorized", "InventoryReserved", "Compensating", "RolledBack", "Completed" },
                    Transitions = new()
                    {
                        new() { FromState = "Draft", ToState = "PaymentAuthorized", EventId = "EVT-PAY-SUCCESS" },
                        new() { FromState = "PaymentAuthorized", ToState = "InventoryReserved", EventId = "EVT-STOCK-LOCKED" },
                        new() { FromState = "InventoryReserved", ToState = "Compensating", EventId = "EVT-SHIP-FAIL" },
                        new() { FromState = "Compensating", ToState = "RolledBack", EventId = "EVT-COMPENSATE" }
                    }
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "ValidateOrder",
                    Steps = new()
                    {
                        new()
                        {
                            StepId = "ValidateOrder",
                            StepType = "Command",
                            NextSteps = new() { { "Default", "AuthorizePayment" } },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "System",
                                    Template = "Validating order {{OrderId}} with amount ${{Amount}}"
                                }
                            }
                        },
                        new()
                        {
                            StepId = "AuthorizePayment",
                            StepType = "Command",
                            NextSteps = new() { { "EVT-PAY-SUCCESS", "ReserveInventory" } },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Webhook",
                                    Target = "https://api.stripe.com/v1/charges/hold",
                                    Template = "Holding payment for order {{OrderId}}",
                                    PayloadMapping = new()
                                    {
                                        { "orderRef", "OrderId" },
                                        { "taxedAmount", "Amount * 1.05" }
                                    },
                                    SignPayload = true
                                }
                            },
                            OnFailure = new()
                            {
                                new()
                                {
                                    ActionType = "Webhook",
                                    Target = "https://api.stripe.com/v1/refunds/void-hold",
                                    Template = "Voiding payment hold for {{OrderId}} due to downstream failure",
                                    PayloadMapping = new() { { "orderRef", "OrderId" } }
                                }
                            }
                        },
                        new()
                        {
                            StepId = "ReserveInventory",
                            StepType = "Command",
                            NextSteps = new() { { "EVT-STOCK-LOCKED", "GenerateShippingLabel" } },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Webhook",
                                    Target = "https://warehouse.internal/api/lock-sku",
                                    PayloadMapping = new()
                                    {
                                        { "sku", "ItemSku" },
                                        { "quantity", "Quantity" }
                                    }
                                }
                            },
                            OnFailure = new()
                            {
                                new()
                                {
                                    ActionType = "Webhook",
                                    Target = "https://warehouse.internal/api/release-sku",
                                    PayloadMapping = new()
                                    {
                                        { "sku", "ItemSku" },
                                        { "quantity", "Quantity" }
                                    }
                                },
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "WarehouseOps",
                                    Template = "Saga rollback: released SKU {{ItemSku}} for canceled order {{OrderId}}"
                                }
                            }
                        },
                        new()
                        {
                            StepId = "GenerateShippingLabel",
                            StepType = "Command",
                            NextSteps = new() { { "Default", "END" }, { "EVT-SHIP-FAIL", "CompensateOrder" } },
                            OnFailure = new()
                            {
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "CustomerSupport",
                                    Template = "Shipping label generation failed for {{OrderId}}. Triggering full Saga rollback."
                                }
                            }
                        },
                        new()
                        {
                            StepId = "CompensateOrder",
                            StepType = "Command",
                            NextSteps = new() { { "EVT-COMPENSATE", "END" } },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "Customer",
                                    Template = "Order {{OrderId}} could not be fulfilled. All holds and inventory reservations were safely rolled back."
                                }
                            }
                        }
                    }
                },
                Roles = SagaRoles(),
                Capabilities = SagaCapabilities()
            };
            WorkflowSimulationGovernance.Apply(sagaBp);

            var sagaWc = new WorkflowClass(clientTenantId, sagaName, "1.0.0", sagaBp);
            manager.Publish(sagaWc);
            manager.SubmitForReview(sagaWc);
            manager.ApproveAsPublic(sagaWc);
            context.WorkflowClasses.Add(sagaWc);

            var sagaDef = WorkflowClassCompiler.MapToRuntimeDefinition(sagaWc);
            sagaDef.Publish();
            context.WorkflowDefinitions.Add(sagaDef);
        }

        // -------------------------------------------------------------------------------------------------
        // FLAGSHIP 2: LoanUnderwritingFlow (Decision Routing, HMAC Webhooks & Senior Underwriter HITL)
        // -------------------------------------------------------------------------------------------------
        const string loanName = "LoanUnderwritingFlow";
        if (!await context.WorkflowClasses.AnyAsync(w => w.TenantId == clientTenantId && w.Name == loanName))
        {
            var loanBp = new WorkflowClassBlueprint
            {
                Events = new()
                {
                    new() { EventId = "EVT-APPLY", Name = "Application Submitted", AllowedRoles = new() { "User", "Employee" } },
                    new() { EventId = "EVT-AUTO-APPROVE", Name = "Fast-Track Auto Approved", AllowedRoles = new() { "System" } },
                    new() { EventId = "EVT-MANUAL-REVIEW", Name = "Requires Underwriter Review", AllowedRoles = new() { "System" } },
                    new() { EventId = "EVT-FINAL-APPROVE", Name = "Underwriter Approved", AllowedRoles = new() { "Manager", "Director" } },
                    new() { EventId = "EVT-DECLINE", Name = "Application Declined", AllowedRoles = new() { "Manager", "Director" } }
                },
                StateMachine = new StateMachineBlueprint
                {
                    InitialState = "Draft",
                    States = new() { "Draft", "Evaluating", "UnderwritingReview", "Approved", "Declined" },
                    Transitions = new()
                    {
                        new() { FromState = "Draft", ToState = "Evaluating", EventId = "EVT-APPLY" },
                        new() { FromState = "Evaluating", ToState = "Approved", EventId = "EVT-AUTO-APPROVE" },
                        new() { FromState = "Evaluating", ToState = "UnderwritingReview", EventId = "EVT-MANUAL-REVIEW" },
                        new() { FromState = "UnderwritingReview", ToState = "Approved", EventId = "EVT-FINAL-APPROVE" },
                        new() { FromState = "UnderwritingReview", ToState = "Declined", EventId = "EVT-DECLINE" },
                        new() { FromState = "Evaluating", ToState = "Declined", EventId = "EVT-DECLINE" }
                    }
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "IntakeApplication",
                    Steps = new()
                    {
                        new()
                        {
                            StepId = "IntakeApplication",
                            StepType = "Command",
                            NextSteps = new() { { "EVT-APPLY", "EvaluateRisk" } },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "Applicant",
                                    Template = "Loan application received for applicant {{ApplicantName}} (Principal: ${{Amount}})"
                                }
                            }
                        },
                        new()
                        {
                            StepId = "EvaluateRisk",
                            StepType = "Decision",
                            Conditions = new()
                            {
                                { "CreditScore >= 720 && DebtToIncome < 0.35", "FastTrackDisbursement" },
                                { "CreditScore < 580", "DeclineApplication" },
                                { "Default", "UnderwriterReview" }
                            }
                        },
                        new()
                        {
                            StepId = "UnderwriterReview",
                            StepType = "HumanTask",
                            RequiredRoles = new() { "Manager", "Director" },
                            NextSteps = new()
                            {
                                { "EVT-FINAL-APPROVE", "DisburseFunds" },
                                { "EVT-DECLINE", "DeclineApplication" }
                            },
                            Sla = new()
                            {
                                Duration = "48h",
                                TimeoutEvent = "EVT-DECLINE",
                                EscalationStepId = "DeclineApplication"
                            },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "Underwriters",
                                    Template = "Manual underwriting required for loan ${{Amount}} (CreditScore: {{CreditScore}})"
                                }
                            }
                        },
                        new()
                        {
                            StepId = "FastTrackDisbursement",
                            StepType = "Command",
                            NextSteps = new() { { "EVT-AUTO-APPROVE", "DisburseFunds" } }
                        },
                        new()
                        {
                            StepId = "DisburseFunds",
                            StepType = "Command",
                            NextSteps = new() { { "Default", "END" } },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Webhook",
                                    Target = "https://core-banking.partner.com/api/v2/disbursements",
                                    Url = "https://core-banking.partner.com/api/v2/disbursements",
                                    Template = "Disbursing ${{Amount}} to recipient account {{AccountNum}}",
                                    PayloadMapping = new()
                                    {
                                        { "applicant", "ApplicantName" },
                                        { "principal", "Amount" },
                                        { "riskTier", "CreditScore >= 720 ? \"Prime\" : \"Standard\"" }
                                    },
                                    SignPayload = true
                                },
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "Applicant",
                                    Template = "Congratulations {{ApplicantName}}! Your loan of ${{Amount}} has been approved and funds are being wired."
                                }
                            },
                            OnFailure = new()
                            {
                                new()
                                {
                                    ActionType = "Webhook",
                                    Target = "https://core-banking.partner.com/api/v2/disbursements/void",
                                    Url = "https://core-banking.partner.com/api/v2/disbursements/void",
                                    Template = "Voiding disbursement for loan application {{ApplicantName}} due to downstream transmission failure.",
                                    PayloadMapping = new()
                                    {
                                        { "applicant", "ApplicantName" },
                                        { "principal", "Amount" },
                                        { "status", "\"Voided\"" }
                                    }
                                }
                            }
                        },
                        new()
                        {
                            StepId = "DeclineApplication",
                            StepType = "Command",
                            NextSteps = new() { { "Default", "END" } },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "Applicant",
                                    Template = "We regret to inform you that your application for ${{Amount}} could not be approved at this time."
                                }
                            }
                        }
                    }
                },
                Roles = LoanRoles(),
                Capabilities = LoanCapabilities()
            };
            WorkflowSimulationGovernance.Apply(loanBp);

            var loanWc = new WorkflowClass(clientTenantId, loanName, "1.0.0", loanBp);
            manager.Publish(loanWc);
            manager.SubmitForReview(loanWc);
            manager.ApproveAsPublic(loanWc);
            context.WorkflowClasses.Add(loanWc);

            var loanDef = WorkflowClassCompiler.MapToRuntimeDefinition(loanWc);
            loanDef.Publish();
            context.WorkflowDefinitions.Add(loanDef);
        }

        // -------------------------------------------------------------------------------------------------
        // FLAGSHIP 3: SecOpsAccessGovernance (HumanTask SLAs, Zero-Zombie Escalation & Auto-Revocation)
        // -------------------------------------------------------------------------------------------------
        const string secOpsName = "SecOpsAccessGovernance";
        if (!await context.WorkflowClasses.AnyAsync(w => w.TenantId == clientTenantId && w.Name == secOpsName))
        {
            var secOpsBp = new WorkflowClassBlueprint
            {
                Events = new()
                {
                    new() { EventId = "EVT-REQUEST-ACCESS", Name = "Request Privileged Access" },
                    new() { EventId = "EVT-APPROVE", Name = "Manager Approved" },
                    new() { EventId = "EVT-ESCALATE", Name = "SLA Breached - Auto Escalated" },
                    new() { EventId = "EVT-DIRECTOR-APPROVE", Name = "SecOps Director Approved" },
                    new() { EventId = "EVT-REVOKE", Name = "Session Expired / Revoked" }
                },
                StateMachine = new StateMachineBlueprint
                {
                    InitialState = "Draft",
                    States = new() { "Draft", "PendingManager", "PendingDirector", "AccessActive", "Revoked" },
                    Transitions = new()
                    {
                        new() { FromState = "Draft", ToState = "PendingManager", EventId = "EVT-REQUEST-ACCESS" },
                        new() { FromState = "PendingManager", ToState = "AccessActive", EventId = "EVT-APPROVE" },
                        new() { FromState = "PendingManager", ToState = "PendingDirector", EventId = "EVT-ESCALATE" },
                        new() { FromState = "PendingDirector", ToState = "AccessActive", EventId = "EVT-DIRECTOR-APPROVE" },
                        new() { FromState = "AccessActive", ToState = "Revoked", EventId = "EVT-REVOKE" }
                    }
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "RequestAccess",
                    Steps = new()
                    {
                        new()
                        {
                            StepId = "RequestAccess",
                            StepType = "Command",
                            NextSteps = new() { { "EVT-REQUEST-ACCESS", "ManagerApproval" } },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "SecurityTeam",
                                    Template = "Access requested by {{UserEmail}} for environment {{Environment}}"
                                }
                            }
                        },
                        new()
                        {
                            StepId = "ManagerApproval",
                            StepType = "HumanTask",
                            RequiredRoles = new() { "Manager" },
                            NextSteps = new()
                            {
                                { "EVT-APPROVE", "ProvisionCredentials" },
                                { "EVT-ESCALATE", "DirectorEscalation" }
                            },
                            Sla = new()
                            {
                                Duration = "24h",
                                TimeoutEvent = "EVT-ESCALATE",
                                EscalationStepId = "DirectorEscalation",
                                EscalationRole = "Director"
                            },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "DirectManager",
                                    Template = "Action Required: Access approval pending for {{UserEmail}} (24h SLA remaining)"
                                }
                            }
                        },
                        new()
                        {
                            StepId = "DirectorEscalation",
                            StepType = "HumanTask",
                            RequiredRoles = new() { "Director" },
                            NextSteps = new()
                            {
                                { "EVT-DIRECTOR-APPROVE", "ProvisionCredentials" }
                            },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "SecOpsDirector",
                                    Template = "SLA BREACH ALERT: Manager did not respond in 24h. Access request for {{UserEmail}} escalated to SecOps Director."
                                }
                            }
                        },
                        new()
                        {
                            StepId = "ProvisionCredentials",
                            StepType = "Command",
                            NextSteps = new() { { "Default", "SessionExpirationTimer" } },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Webhook",
                                    Target = "https://iam.internal/api/v1/temp-creds",
                                    Url = "https://iam.internal/api/v1/temp-creds",
                                    Template = "Provisioning temporary 8h IAM credentials for {{UserEmail}}",
                                    PayloadMapping = new()
                                    {
                                        { "user", "UserEmail" },
                                        { "scope", "Environment" },
                                        { "ttlHours", "8" }
                                    },
                                    SignPayload = true
                                },
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "User",
                                    Template = "Temporary access to {{Environment}} granted for 8 hours."
                                }
                            },
                            OnFailure = new()
                            {
                                new()
                                {
                                    ActionType = "Webhook",
                                    Target = "https://iam.internal/api/v1/revoke-creds",
                                    Url = "https://iam.internal/api/v1/revoke-creds",
                                    Template = "Revoking credentials due to provisioning error for {{UserEmail}}",
                                    PayloadMapping = new() { { "user", "UserEmail" } }
                                }
                            }
                        },
                        new()
                        {
                            StepId = "SessionExpirationTimer",
                            StepType = "Timer",
                            NextSteps = new() { { "EVT-REVOKE", "RevokeAccess" } },
                            Sla = new()
                            {
                                Duration = "8h",
                                TimeoutEvent = "EVT-REVOKE"
                            }
                        },
                        new()
                        {
                            StepId = "RevokeAccess",
                            StepType = "Command",
                            NextSteps = new() { { "Default", "END" } },
                            OnEntry = new()
                            {
                                new()
                                {
                                    ActionType = "Webhook",
                                    Target = "https://iam.internal/api/v1/revoke-creds",
                                    Url = "https://iam.internal/api/v1/revoke-creds",
                                    Template = "Revoking credentials for {{UserEmail}} upon 8h timer expiration",
                                    PayloadMapping = new() { { "user", "UserEmail" } }
                                },
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "User",
                                    Template = "Your temporary access session for {{Environment}} has expired and credentials have been revoked."
                                }
                            },
                            OnFailure = new()
                            {
                                new()
                                {
                                    ActionType = "Notification",
                                    Target = "SecOpsOnCall",
                                    Template = "CRITICAL: Automated revocation failed for {{UserEmail}}. Immediate manual intervention required."
                                }
                            }
                        }
                    }
                },
                Roles = SecOpsRoles(),
                Capabilities = SecOpsCapabilities()
            };
            WorkflowSimulationGovernance.Apply(secOpsBp);

            var secOpsWc = new WorkflowClass(clientTenantId, secOpsName, "1.0.0", secOpsBp);
            manager.Publish(secOpsWc);
            manager.SubmitForReview(secOpsWc);
            manager.ApproveAsPublic(secOpsWc);
            context.WorkflowClasses.Add(secOpsWc);

            var secOpsDef = WorkflowClassCompiler.MapToRuntimeDefinition(secOpsWc);
            secOpsDef.Publish();
            context.WorkflowDefinitions.Add(secOpsDef);
        }

        // -------------------------------------------------------------------------------------------------
        // FLAGSHIP 4: IncidentAlertEscalation (SLA reminder alerts + timeout / severity escalation)
        // -------------------------------------------------------------------------------------------------
        const string incidentName = "IncidentAlertEscalation";
        if (!await context.WorkflowClasses.AnyAsync(w => w.TenantId == clientTenantId && w.Name == incidentName))
        {
            var incidentBp = CreateIncidentAlertEscalationBlueprint();
            var incidentWc = new WorkflowClass(clientTenantId, incidentName, "1.0.0", incidentBp);
            manager.Publish(incidentWc);
            manager.SubmitForReview(incidentWc);
            manager.ApproveAsPublic(incidentWc);
            context.WorkflowClasses.Add(incidentWc);

            var incidentDef = WorkflowClassCompiler.MapToRuntimeDefinition(incidentWc);
            incidentDef.Publish();
            context.WorkflowDefinitions.Add(incidentDef);
        }

        // -------------------------------------------------------------------------------------------------
        // FLAGSHIP 5: QuoteAutoReview (AI Agent actor auto-commits in-bound quotes)
        // -------------------------------------------------------------------------------------------------
        const string quoteName = "QuoteAutoReview";
        if (!await context.WorkflowClasses.AnyAsync(w => w.TenantId == clientTenantId && w.Name == quoteName))
        {
            var quoteBp = CreateQuoteAutoReviewBlueprint();
            var quoteWc = new WorkflowClass(clientTenantId, quoteName, "1.0.0", quoteBp);
            manager.Publish(quoteWc);
            manager.SubmitForReview(quoteWc);
            manager.ApproveAsPublic(quoteWc);
            context.WorkflowClasses.Add(quoteWc);

            var quoteDef = WorkflowClassCompiler.MapToRuntimeDefinition(quoteWc);
            quoteDef.Publish();
            context.WorkflowDefinitions.Add(quoteDef);
        }

        await RepairExpenseApprovalV2GraphAsync(context, clientTenantId);
        await context.SaveChangesAsync();
    }

    private static CapabilityBlueprint Cap(string code, string description) =>
        new() { Code = code, Description = description };

    private static RoleBlueprint BizRole(string name, string description, params string[] capabilities) =>
        new()
        {
            Name = name,
            Description = description,
            GrantedCapabilities = capabilities.ToList(),
            ResolutionType = "Assignment"
        };

    private static List<CapabilityBlueprint> ExpenseCapabilities() =>
    [
        Cap("workflow.read", "Read expense instance state"),
        Cap("event.publish.EVT-SUBMIT", "Submit an expense"),
        Cap("event.publish.EVT-APPROVE", "Approve an expense"),
        Cap("event.publish.EVT-REJECT", "Reject an expense")
    ];

    private static List<RoleBlueprint> ExpenseRoles() =>
    [
        BizRole("Submitter", "Files the expense request", "workflow.read", "event.publish.EVT-SUBMIT"),
        BizRole("Approver", "Approves or rejects the expense", "workflow.read", "event.publish.EVT-APPROVE", "event.publish.EVT-REJECT")
    ];

    private static List<CapabilityBlueprint> ExpenseV2Capabilities() =>
    [
        Cap("workflow.read", "Read expense instance state"),
        Cap("event.publish.EVT-SUBMIT", "Submit an expense"),
        Cap("event.publish.EVT-APPROVE", "Manager approves a low-value expense"),
        Cap("event.publish.EVT-REJECT", "Manager rejects an expense"),
        Cap("event.publish.EVT-ESCALATE", "Escalate a high-value expense to Director"),
        Cap("event.publish.EVT-DIRECTOR-APPROVE", "Director approves an escalated expense"),
        Cap("event.publish.EVT-DIRECTOR-REJECT", "Director rejects an escalated expense")
    ];

    private static List<RoleBlueprint> ExpenseV2Roles() =>
    [
        BizRole("Submitter", "Files the expense request", "workflow.read", "event.publish.EVT-SUBMIT"),
        BizRole("Manager", "First-line manager review", "workflow.read", "event.publish.EVT-APPROVE", "event.publish.EVT-REJECT", "event.publish.EVT-ESCALATE"),
        BizRole("Director", "Second-line approval for high-value expenses", "workflow.read", "event.publish.EVT-DIRECTOR-APPROVE", "event.publish.EVT-DIRECTOR-REJECT")
    ];

    private static List<CapabilityBlueprint> SagaCapabilities() =>
    [
        Cap("workflow.read", "Read order saga instance state"),
        Cap("event.publish.EVT-VALIDATE", "Validate the order"),
        Cap("event.publish.EVT-PAY-SUCCESS", "Confirm payment authorization"),
        Cap("event.publish.EVT-STOCK-LOCKED", "Confirm inventory reservation"),
        Cap("event.publish.EVT-SHIP-FAIL", "Signal shipping failure"),
        Cap("event.publish.EVT-COMPENSATE", "Complete saga rollback")
    ];

    private static List<RoleBlueprint> SagaRoles() =>
    [
        BizRole("OrderClerk", "Owns order intake and payment authorization", "workflow.read", "event.publish.EVT-VALIDATE", "event.publish.EVT-PAY-SUCCESS"),
        BizRole("Warehouse", "Reserves and releases inventory", "workflow.read", "event.publish.EVT-STOCK-LOCKED"),
        BizRole("Logistics", "Ships the order or triggers saga compensation", "workflow.read", "event.publish.EVT-SHIP-FAIL", "event.publish.EVT-COMPENSATE")
    ];

    private static List<CapabilityBlueprint> LoanCapabilities() =>
    [
        Cap("workflow.read", "Read loan instance state"),
        Cap("event.publish.EVT-APPLY", "Submit a loan application"),
        Cap("event.publish.EVT-AUTO-APPROVE", "Auto-approve a prime application"),
        Cap("event.publish.EVT-MANUAL-REVIEW", "Send an application to underwriting"),
        Cap("event.publish.EVT-FINAL-APPROVE", "Approve after underwriter review"),
        Cap("event.publish.EVT-DECLINE", "Decline a loan application")
    ];

    private static List<RoleBlueprint> LoanRoles() =>
    [
        BizRole("Applicant", "Submits a loan application", "workflow.read", "event.publish.EVT-APPLY"),
        BizRole("Manager", "Manual underwriter who can approve or decline", "workflow.read", "event.publish.EVT-MANUAL-REVIEW", "event.publish.EVT-FINAL-APPROVE", "event.publish.EVT-DECLINE"),
        BizRole("Director", "Senior underwriter with the same decision rights", "workflow.read", "event.publish.EVT-FINAL-APPROVE", "event.publish.EVT-DECLINE")
    ];

    private static List<CapabilityBlueprint> SecOpsCapabilities() =>
    [
        Cap("workflow.read", "Read access-governance instance state"),
        Cap("event.publish.EVT-REQUEST-ACCESS", "Request privileged access"),
        Cap("event.publish.EVT-APPROVE", "Manager approves access"),
        Cap("event.publish.EVT-ESCALATE", "Escalate after SLA breach"),
        Cap("event.publish.EVT-DIRECTOR-APPROVE", "Director approves escalated access"),
        Cap("event.publish.EVT-REVOKE", "Revoke temporary credentials")
    ];

    private static List<RoleBlueprint> SecOpsRoles() =>
    [
        BizRole("Requester", "Asks for privileged access", "workflow.read", "event.publish.EVT-REQUEST-ACCESS"),
        BizRole("Manager", "Approves access or lets SLA escalate", "workflow.read", "event.publish.EVT-APPROVE", "event.publish.EVT-ESCALATE"),
        BizRole("Director", "Approves after a 24h SLA breach", "workflow.read", "event.publish.EVT-DIRECTOR-APPROVE"),
        BizRole("SecOps", "Owns credential provisioning and revocation", "workflow.read", "event.publish.EVT-REVOKE")
    ];

    private const string CriticalIncidentCondition = "Severity == \"Critical\"";

    private static List<CapabilityBlueprint> IncidentCapabilities() =>
    [
        Cap("workflow.read", "Read incident instance state"),
        Cap("event.publish.EVT-OPEN", "Open an incident ticket"),
        Cap("event.publish.EVT-RESOLVE", "L1 resolves an incident before SLA breach"),
        Cap("event.publish.EVT-ESCALATE", "Escalate an incident after SLA breach or L1 handoff"),
        Cap("event.publish.EVT-CLOSE", "On-call closes an escalated incident")
    ];

    private static List<RoleBlueprint> IncidentRoles() =>
    [
        BizRole("Reporter", "Opens an incident", "workflow.read", "event.publish.EVT-OPEN"),
        BizRole("Support", "L1 inbox: resolve in SLA or escalate", "workflow.read", "event.publish.EVT-RESOLVE", "event.publish.EVT-ESCALATE"),
        BizRole("OnCall", "L2 inbox after alert escalation", "workflow.read", "event.publish.EVT-CLOSE")
    ];

    private const string HighValueQuoteCondition = "Amount > 1500";

    private static List<CapabilityBlueprint> QuoteCapabilities() =>
    [
        Cap("workflow.read", "Read quote instance state"),
        Cap("event.publish.EVT-SUBMIT", "Submit a quote for review"),
        Cap("event.publish.EVT-ACCEPT", "Accept an in-bound quote"),
        Cap("event.publish.EVT-REQUEST-REVISION", "Send a quote back for revision")
    ];

    private static List<RoleBlueprint> QuoteRoles() =>
    [
        BizRole("Submitter", "Files the quote", "workflow.read", "event.publish.EVT-SUBMIT"),
        BizRole("QuoteAgent", "Hosted agent inbox for in-bound quotes", "workflow.read", "event.publish.EVT-ACCEPT"),
        BizRole("Advisor", "Human override and over-limit review", "workflow.read", "event.publish.EVT-ACCEPT", "event.publish.EVT-REQUEST-REVISION")
    ];

    private static void EnsureStepRequiredRoles(WorkflowClassBlueprint blueprint, string stepId, params string[] roles)
    {
        var step = blueprint.Workflow.Steps.FirstOrDefault(s => s.StepId == stepId);
        if (step == null) return;
        foreach (var role in roles)
        {
            if (!step.RequiredRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
                step.RequiredRoles.Add(role);
        }
    }

    private static void EnsureCapabilityList(List<CapabilityBlueprint> target, IEnumerable<CapabilityBlueprint> source)
    {
        foreach (var capability in source)
        {
            if (!target.Any(existing => string.Equals(existing.Code, capability.Code, StringComparison.OrdinalIgnoreCase)))
                target.Add(capability);
        }
    }

    private static void EnsureRoleList(List<RoleBlueprint> target, IEnumerable<RoleBlueprint> source)
    {
        foreach (var role in source)
        {
            if (!target.Any(existing => string.Equals(existing.Name, role.Name, StringComparison.OrdinalIgnoreCase)))
                target.Add(role);
        }
    }

    private static void EnsureSampleRoleVocabulary(WorkflowClass workflowClass)
    {
        var blueprint = workflowClass.Definition;
        switch (workflowClass.Name)
        {
            case "ExpenseApproval":
                EnsureStepRequiredRoles(blueprint, "Pending", "Manager");
                EnsureRoleList(blueprint.Roles, ExpenseRoles());
                EnsureCapabilityList(blueprint.Capabilities, ExpenseCapabilities());
                WorkflowSimulationGovernance.Apply(blueprint);
                break;
            case "ExpenseApprovalV2":
                RepairExpenseApprovalV2Blueprint(blueprint);
                EnsureStepRequiredRoles(blueprint, "PendingManager", "Manager");
                EnsureStepRequiredRoles(blueprint, "PendingDirector", "Director");
                EnsureRoleList(blueprint.Roles, ExpenseV2Roles());
                EnsureCapabilityList(blueprint.Capabilities, ExpenseV2Capabilities());
                WorkflowSimulationGovernance.Apply(blueprint);
                break;
            case "OrderSagaFulfillment":
                EnsureRoleList(blueprint.Roles, SagaRoles());
                EnsureCapabilityList(blueprint.Capabilities, SagaCapabilities());
                WorkflowSimulationGovernance.Apply(blueprint);
                break;
            case "LoanUnderwritingFlow":
                EnsureStepRequiredRoles(blueprint, "UnderwriterReview", "Manager", "Director");
                EnsureRoleList(blueprint.Roles, LoanRoles());
                EnsureCapabilityList(blueprint.Capabilities, LoanCapabilities());
                WorkflowSimulationGovernance.Apply(blueprint);
                break;
            case "SecOpsAccessGovernance":
                EnsureStepRequiredRoles(blueprint, "ManagerApproval", "Manager");
                EnsureStepRequiredRoles(blueprint, "DirectorEscalation", "Director");
                EnsureRoleList(blueprint.Roles, SecOpsRoles());
                EnsureCapabilityList(blueprint.Capabilities, SecOpsCapabilities());
                WorkflowSimulationGovernance.Apply(blueprint);
                break;
            case "IncidentAlertEscalation":
                EnsureStepRequiredRoles(blueprint, "L1Review", "Support");
                EnsureStepRequiredRoles(blueprint, "OnCallEscalation", "OnCall");
                EnsureRoleList(blueprint.Roles, IncidentRoles());
                EnsureCapabilityList(blueprint.Capabilities, IncidentCapabilities());
                WorkflowSimulationGovernance.Apply(blueprint);
                break;
            case "QuoteAutoReview":
                EnsureStepRequiredRoles(blueprint, "AgentReview", "QuoteAgent");
                EnsureStepRequiredRoles(blueprint, "AdvisorReview", "Advisor");
                EnsureRoleList(blueprint.Roles, QuoteRoles());
                EnsureCapabilityList(blueprint.Capabilities, QuoteCapabilities());
                WorkflowSimulationGovernance.Apply(blueprint);
                break;
        }
    }

    private static async Task RepairSandboxSampleDeclarationsAsync(FlowOSDbContext context, Guid tenantId)
    {
        foreach (var name in SandboxSampleWorkflowNames)
        {
            var workflowClass = await context.WorkflowClasses.FirstOrDefaultAsync(
                w => w.TenantId == tenantId && w.Name == name);
            if (workflowClass == null)
                continue;

            EnsureSampleRoleVocabulary(workflowClass);
            context.Entry(workflowClass).Property(w => w.Definition).IsModified = true;

            var definition = await context.WorkflowDefinitions
                .Include(d => d.Steps)
                .Where(d => d.TenantId == tenantId && d.Name == name)
                .OrderByDescending(d => d.Version)
                .FirstOrDefaultAsync();
            if (definition == null)
                continue;

            foreach (var stepBlueprint in workflowClass.Definition.Workflow.Steps)
            {
                var step = definition.Steps.FirstOrDefault(s => s.StepId == stepBlueprint.StepId);
                if (step == null || stepBlueprint.RequiredRoles.Count == 0)
                    continue;
                step.AllowedRoles = stepBlueprint.RequiredRoles.ToList();
            }

            var previousStatus = definition.Status;
            SetPrivateProperty(definition, "Status", WorkflowStatus.Draft);
            WorkflowClassCompiler.ApplyTemplateBusinessRoles(definition, workflowClass.Definition);
            SetPrivateProperty(definition, "Status", previousStatus);
        }

        await context.SaveChangesAsync();
    }

    public static async Task SeedSampleBusinessContextsAsync(FlowOSDbContext context, Guid tenantId)
    {
        foreach (var name in SandboxSampleWorkflowNames)
        {
            try
            {
                var workflowClass = await context.WorkflowClasses.FirstOrDefaultAsync(
                    w => w.TenantId == tenantId && w.Name == name);
                if (workflowClass == null)
                    continue;

                var spec = SampleContextSpec(name);
                var existingRevisions = await context.WorkflowContextBindingRevisions
                    .Where(revision => revision.SourceWorkflowClassId == workflowClass.Id)
                    .ToListAsync();
                if (existingRevisions.Count > 0)
                {
                    await EnsureSampleBindingVocabularyAsync(context, workflowClass, spec, existingRevisions);
                    continue;
                }

                if (await context.WorkflowContextBindings.AnyAsync(binding =>
                        binding.TenantId == tenantId &&
                        (binding.NormalizedContextType == spec.ContextType.ToUpperInvariant() ||
                         binding.NormalizedName == spec.BindingName.ToUpperInvariant())))
                {
                    continue;
                }

                var binding = new WorkflowContextBinding(tenantId, spec.ContextType, spec.BindingName);
                var revision = new WorkflowContextBindingRevision(
                    binding.Id,
                    1,
                    workflowClass.Id,
                    workflowClass.Version,
                    CreateSampleContextDefinition(workflowClass, spec));
                binding.SetDraftRevision(revision.Id);

                var package = WorkflowClassCompiler.MapToContextRuntimePackage(workflowClass, binding, revision);
                revision.Activate(package.WorkflowDefinition.Id, package.StateMachineDefinition.Id, package.ContentHash);
                binding.Activate(revision.Id);

                context.WorkflowContextBindings.Add(binding);
                context.WorkflowContextBindingRevisions.Add(revision);
                context.StateMachineDefinitions.Add(package.StateMachineDefinition);
                context.WorkflowDefinitions.Add(package.WorkflowDefinition);

                foreach (var eventDefinition in package.EventDefinitions)
                {
                    var exists = await context.EventDefinitions.AnyAsync(existing =>
                        existing.TenantId == tenantId && existing.EventId == eventDefinition.EventId);
                    if (!exists)
                        context.EventDefinitions.Add(eventDefinition);
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataSeeder] Skipping business context for '{name}': {ex.Message}");
            }
        }
    }

    private static (string ContextType, string BindingName, string EntityType, Dictionary<string, string> InputMapping, Dictionary<string, JsonElement> ConditionParameters)
        SampleContextSpec(string workflowName) => workflowName switch
    {
        "ExpenseApproval" => (
            "Expense",
            "Expense Approval Context",
            "ExpenseEntity",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Amount"] = "amount",
                ["Description"] = "justification"
            },
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ApprovalLimit"] = JsonSerializer.SerializeToElement(5000)
            }),
        "ExpenseApprovalV2" => (
            "ExpenseV2",
            "Expense Approval V2 Context",
            "ExpenseEntityV2",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Amount"] = "amount",
                ["Description"] = "justification"
            },
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ApprovalLimit"] = JsonSerializer.SerializeToElement(100)
            }),
        "OrderSagaFulfillment" => (
            "Order",
            "Order Saga Context",
            "OrderEntity",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["OrderId"] = "orderId",
                ["Amount"] = "amount",
                ["ItemSku"] = "itemSku",
                ["Quantity"] = "quantity"
            },
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)),
        "LoanUnderwritingFlow" => (
            "Loan",
            "Loan Underwriting Context",
            "LoanApplication",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ApplicantName"] = "applicantName",
                ["Amount"] = "amount",
                ["CreditScore"] = "creditScore",
                ["DebtToIncome"] = "debtToIncome",
                ["AccountNum"] = "accountNum"
            },
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)),
        "SecOpsAccessGovernance" => (
            "Access",
            "Privileged Access Context",
            "AccessRequest",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["UserEmail"] = "userEmail",
                ["Environment"] = "environment"
            },
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)),
        "IncidentAlertEscalation" => (
            "Incident",
            "Incident Alert Context",
            "IncidentTicket",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TicketId"] = "ticketId",
                ["Severity"] = "severity",
                ["Summary"] = "summary"
            },
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)),
        "QuoteAutoReview" => (
            "Quote",
            "Quote Auto Review Context",
            "ServiceQuote",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["QuoteId"] = "quoteId",
                ["Amount"] = "amount",
                ["Estimate"] = "estimate"
            },
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ApprovalLimit"] = JsonSerializer.SerializeToElement(1500)
            }),
        _ => (
            workflowName,
            $"{workflowName} Context",
            $"{workflowName}Entity",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase))
    };

    private static WorkflowContextBindingDefinition CreateSampleContextDefinition(
        WorkflowClass workflowClass,
        (string ContextType, string BindingName, string EntityType, Dictionary<string, string> InputMapping, Dictionary<string, JsonElement> ConditionParameters) spec)
    {
        var roleOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in workflowClass.Definition.Roles)
        {
            if (string.IsNullOrWhiteSpace(role.Name))
                continue;
            roleOverrides[role.Name.Trim()] = role.Name.Trim();
        }

        var capabilityOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var capability in workflowClass.Definition.Capabilities)
        {
            if (string.IsNullOrWhiteSpace(capability.Code))
                continue;
            capabilityOverrides[capability.Code.Trim()] = capability.Code.Trim();
        }

        return new WorkflowContextBindingDefinition
        {
            EntityType = spec.EntityType,
            RoleOverrides = roleOverrides,
            CapabilityOverrides = capabilityOverrides,
            InputMapping = spec.InputMapping,
            ConditionParameters = spec.ConditionParameters,
            PolicyGuideline = SamplePolicyGuideline(workflowClass.Name),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["templateRoles"] = string.Join(",", roleOverrides.Keys),
                ["templateCapabilities"] = string.Join(",", capabilityOverrides.Keys)
            }
        };
    }

    public const string QuoteAutoReviewPolicyGuideline =
        "ServiceQuote overlay: never auto-accept when Amount is above ApprovalLimit even if the template would accept.";

    private static string? SamplePolicyGuideline(string workflowName) =>
        string.Equals(workflowName, "QuoteAutoReview", StringComparison.OrdinalIgnoreCase)
            ? QuoteAutoReviewPolicyGuideline
            : null;

    private static async Task EnsureSampleBindingVocabularyAsync(
        FlowOSDbContext context,
        WorkflowClass workflowClass,
        (string ContextType, string BindingName, string EntityType, Dictionary<string, string> InputMapping, Dictionary<string, JsonElement> ConditionParameters) spec,
        IReadOnlyCollection<WorkflowContextBindingRevision> revisions)
    {
        var expected = CreateSampleContextDefinition(workflowClass, spec);
        var changed = false;
        foreach (var revision in revisions)
        {
            var merged = MergeSampleBindingVocabulary(revision.Definition, expected);
            if (!SampleBindingVocabularyChanged(revision.Definition, merged))
                continue;

            SetPrivateProperty(revision, nameof(WorkflowContextBindingRevision.Definition), merged);
            context.Entry(revision).Property(item => item.Definition).IsModified = true;
            changed = true;

            if (!revision.WorkflowDefinitionId.HasValue)
                continue;

            var definition = await context.WorkflowDefinitions.FirstOrDefaultAsync(item =>
                item.Id == revision.WorkflowDefinitionId.Value);
            if (definition == null)
                continue;

            var previousStatus = definition.Status;
            SetPrivateProperty(definition, nameof(WorkflowDefinition.Status), WorkflowStatus.Draft);
            WorkflowClassCompiler.ApplyTemplateBusinessRoles(definition, workflowClass.Definition, merged);
            SetPrivateProperty(definition, nameof(WorkflowDefinition.Status), previousStatus);
        }

        if (changed)
            await context.SaveChangesAsync();
    }

    private static WorkflowContextBindingDefinition MergeSampleBindingVocabulary(
        WorkflowContextBindingDefinition current,
        WorkflowContextBindingDefinition expected)
    {
        var roleOverrides = new Dictionary<string, string>(current.RoleOverrides, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in expected.RoleOverrides)
        {
            if (!roleOverrides.ContainsKey(pair.Key))
                roleOverrides[pair.Key] = pair.Value;
        }

        var capabilityOverrides = new Dictionary<string, string>(current.CapabilityOverrides, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in expected.CapabilityOverrides)
        {
            if (!capabilityOverrides.ContainsKey(pair.Key))
                capabilityOverrides[pair.Key] = pair.Value;
        }

        var inputMapping = new Dictionary<string, string>(current.InputMapping, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in expected.InputMapping)
        {
            if (!inputMapping.ContainsKey(pair.Key))
                inputMapping[pair.Key] = pair.Value;
        }

        var metadata = new Dictionary<string, string>(current.Metadata, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in expected.Metadata)
            metadata[pair.Key] = pair.Value;

        return current with
        {
            EntityType = string.IsNullOrWhiteSpace(current.EntityType) ? expected.EntityType : current.EntityType,
            RoleOverrides = roleOverrides,
            CapabilityOverrides = capabilityOverrides,
            InputMapping = inputMapping,
            Metadata = metadata,
            PolicyGuideline = string.IsNullOrWhiteSpace(current.PolicyGuideline)
                ? expected.PolicyGuideline
                : current.PolicyGuideline
        };
    }

    private static bool SampleBindingVocabularyChanged(
        WorkflowContextBindingDefinition current,
        WorkflowContextBindingDefinition merged)
        => merged.RoleOverrides.Count != current.RoleOverrides.Count ||
           merged.CapabilityOverrides.Count != current.CapabilityOverrides.Count ||
           merged.InputMapping.Count != current.InputMapping.Count ||
           !string.Equals(merged.EntityType, current.EntityType, StringComparison.Ordinal) ||
           !string.Equals(merged.PolicyGuideline, current.PolicyGuideline, StringComparison.Ordinal) ||
           merged.Metadata.GetValueOrDefault("templateRoles") != current.Metadata.GetValueOrDefault("templateRoles") ||
           merged.Metadata.GetValueOrDefault("templateCapabilities") != current.Metadata.GetValueOrDefault("templateCapabilities");

    private static readonly string[] SandboxSampleWorkflowNames =
    {
        "ExpenseApproval",
        "ExpenseApprovalV2",
        "OrderSagaFulfillment",
        "LoanUnderwritingFlow",
        "SecOpsAccessGovernance",
        "IncidentAlertEscalation",
        "QuoteAutoReview"
    };

    private static async Task EnsureRoleWithPermissionsAsync(
        FlowOSDbContext context,
        Guid tenantId,
        string roleName,
        params string[] permissions)
    {
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Name == roleName && r.TenantId == tenantId);
        if (role == null)
        {
            role = new Role(tenantId, roleName);
            context.Roles.Add(role);
        }

        foreach (var permission in permissions)
            role.AddPermission(permission);
    }

    private static async Task EnsurePublishedRuntimeDefinitionsAsync(FlowOSDbContext context, Guid tenantId)
    {
        foreach (var name in SandboxSampleWorkflowNames)
        {
            try
            {
                var existing = await context.WorkflowDefinitions
                    .Where(d => d.TenantId == tenantId && d.Name == name)
                    .OrderByDescending(d => d.Version)
                    .FirstOrDefaultAsync();
                if (existing != null)
                {
                    if (existing.Status != WorkflowStatus.Published)
                    {
                        existing.Publish();
                        await context.SaveChangesAsync();
                    }
                    continue;
                }

                var workflowClass = await context.WorkflowClasses.FirstOrDefaultAsync(
                    w => w.TenantId == tenantId && w.Name == name);
                if (workflowClass == null)
                    continue;

                var definition = WorkflowClassCompiler.MapToRuntimeDefinition(workflowClass);
                if (definition.Status != WorkflowStatus.Published)
                    definition.Publish();
                context.WorkflowDefinitions.Add(definition);
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataSeeder] Skipping runtime definition for '{name}': {ex.Message}");
            }
        }
    }

    public static async Task RepairExpenseApprovalV2GraphAsync(FlowOSDbContext context, Guid tenantId)
    {
        var workflowClass = await context.WorkflowClasses.FirstOrDefaultAsync(
            item => item.TenantId == tenantId && item.Name == "ExpenseApprovalV2");
        if (workflowClass == null)
            return;

        if (ExpenseV2GraphNeedsRebuild(workflowClass.Definition))
        {
            SetPrivateProperty(workflowClass, nameof(WorkflowClass.Definition), CreateExpenseApprovalV2Blueprint());
        }
        else
        {
            RepairExpenseApprovalV2Blueprint(workflowClass.Definition);
        }

        EnsureSampleRoleVocabulary(workflowClass);
        context.Entry(workflowClass).Property(item => item.Definition).IsModified = true;

        var definitions = await context.WorkflowDefinitions
            .Include(item => item.Steps)
            .Where(item => item.TenantId == tenantId &&
                (item.Name == "ExpenseApprovalV2" ||
                 item.Name == "Expense Approval V2 Context" ||
                 item.SourceWorkflowClassId == workflowClass.Id))
            .ToListAsync();

        foreach (var definition in definitions)
        {
            RepairDeadCheckAmountHop(definition);
            foreach (var stepBlueprint in workflowClass.Definition.Workflow.Steps)
            {
                var step = definition.Steps.FirstOrDefault(item =>
                    string.Equals(item.StepId, stepBlueprint.StepId, StringComparison.OrdinalIgnoreCase));
                if (step == null || stepBlueprint.RequiredRoles.Count == 0)
                    continue;
                step.AllowedRoles = stepBlueprint.RequiredRoles.ToList();
            }
        }

        await context.SaveChangesAsync();
    }

    private const string HighValueExpenseCondition = "Amount > 5000";

    private static bool ExpenseV2GraphNeedsRebuild(WorkflowClassBlueprint blueprint)
    {
        var pending = blueprint.Workflow.Steps.FirstOrDefault(step =>
            string.Equals(step.StepId, "PendingManager", StringComparison.OrdinalIgnoreCase));
        var director = blueprint.Workflow.Steps.FirstOrDefault(step =>
            string.Equals(step.StepId, "PendingDirector", StringComparison.OrdinalIgnoreCase));
        return pending == null ||
               !string.Equals(pending.StepType, "HumanTask", StringComparison.OrdinalIgnoreCase) ||
               director == null ||
               !string.Equals(director.StepType, "HumanTask", StringComparison.OrdinalIgnoreCase);
    }

    private static void RepairExpenseApprovalV2Blueprint(WorkflowClassBlueprint blueprint)
    {
        var steps = blueprint.Workflow.Steps;
        steps.RemoveAll(step => string.Equals(step.StepId, "CheckAmount", StringComparison.OrdinalIgnoreCase));

        var draft = steps.FirstOrDefault(step =>
            string.Equals(step.StepId, "Draft", StringComparison.OrdinalIgnoreCase));
        var insertAt = draft == null ? 0 : Math.Min(steps.IndexOf(draft) + 1, steps.Count);
        steps.Insert(insertAt, new StepBlueprint
        {
            StepId = "CheckAmount",
            StepType = "Decision",
            RequiredRoles = new() { "System" },
            Conditions = new()
            {
                { HighValueExpenseCondition, "PendingDirector" },
                { "Default", "PendingManager" }
            },
            NextSteps = new() { { "Default", "PendingManager" } }
        });

        if (draft != null)
            draft.NextSteps["EVT-SUBMIT"] = "CheckAmount";

        var transitions = blueprint.StateMachine.Transitions;
        transitions.RemoveAll(item =>
            string.Equals(item.FromState, "Draft", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.EventId, "EVT-SUBMIT", StringComparison.OrdinalIgnoreCase));
        transitions.Insert(0, new TransitionBlueprint
        {
            FromState = "Draft",
            ToState = "PendingDirector",
            EventId = "EVT-SUBMIT",
            Condition = HighValueExpenseCondition
        });
        transitions.Insert(1, new TransitionBlueprint
        {
            FromState = "Draft",
            ToState = "PendingManager",
            EventId = "EVT-SUBMIT"
        });
    }

    private static void RepairDeadCheckAmountHop(WorkflowDefinition definition)
    {
        var previousStatus = definition.Status;
        SetPrivateProperty(definition, nameof(WorkflowDefinition.Status), WorkflowStatus.Draft);

        var check = definition.Steps.FirstOrDefault(step =>
            string.Equals(step.StepId, "CheckAmount", StringComparison.OrdinalIgnoreCase));
        if (check == null)
        {
            definition.AddStep(new WorkflowStepDefinition("CheckAmount", WorkflowStepType.Decision)
            {
                AllowedRoles = new() { "System" },
                Conditions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [HighValueExpenseCondition] = "PendingDirector",
                    ["Default"] = "PendingManager"
                },
                NextSteps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Default"] = "PendingManager"
                }
            });
        }
        else
        {
            check.StepType = WorkflowStepType.Decision;
            check.Conditions[HighValueExpenseCondition] = "PendingDirector";
            check.Conditions["Default"] = "PendingManager";
            check.NextSteps["Default"] = "PendingManager";
        }

        var draftStep = definition.Steps.FirstOrDefault(step =>
            string.Equals(step.StepId, "Draft", StringComparison.OrdinalIgnoreCase));
        if (draftStep != null)
            draftStep.NextSteps["EVT-SUBMIT"] = "CheckAmount";

        SetPrivateProperty(definition, nameof(WorkflowDefinition.Status), previousStatus);
    }

    public static WorkflowClassBlueprint CreateIncidentAlertEscalationBlueprint()
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new()
            {
                new EventBlueprint { EventId = "EVT-OPEN", Name = "Open Incident", Category = EventCategory.Human, AllowedRoles = new() { "Reporter" } },
                new EventBlueprint { EventId = "EVT-SLA-WARN-1H", Name = "1h SLA warning alert", Category = EventCategory.System },
                new EventBlueprint { EventId = "EVT-SLA-WARN-3H", Name = "3h SLA warning alert", Category = EventCategory.System },
                new EventBlueprint { EventId = "EVT-ESCALATE", Name = "Escalate to On-Call", Category = EventCategory.Human, AllowedRoles = new() { "Support" } },
                new EventBlueprint { EventId = "EVT-RESOLVE", Name = "L1 Resolve", Category = EventCategory.Human, AllowedRoles = new() { "Support" } },
                new EventBlueprint { EventId = "EVT-CLOSE", Name = "On-Call Close", Category = EventCategory.Human, AllowedRoles = new() { "OnCall" } }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = new() { "Draft", "L1Queued", "Escalated", "Resolved" },
                Transitions = new()
                {
                    new TransitionBlueprint { FromState = "Draft", ToState = "Escalated", EventId = "EVT-OPEN", Condition = CriticalIncidentCondition },
                    new TransitionBlueprint { FromState = "Draft", ToState = "L1Queued", EventId = "EVT-OPEN" },
                    new TransitionBlueprint { FromState = "L1Queued", ToState = "Resolved", EventId = "EVT-RESOLVE" },
                    new TransitionBlueprint { FromState = "L1Queued", ToState = "Escalated", EventId = "EVT-ESCALATE" },
                    new TransitionBlueprint { FromState = "Escalated", ToState = "Resolved", EventId = "EVT-CLOSE" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "ReportIncident",
                Steps = new()
                {
                    new StepBlueprint
                    {
                        StepId = "ReportIncident",
                        StepType = "Command",
                        NextSteps = new() { { "EVT-OPEN", "CheckSeverity" } },
                        OnEntry = new()
                        {
                            new StepActionBlueprint
                            {
                                ActionType = "Notification",
                                Target = "IncidentChannel",
                                Template = "ALERT: Incident {{TicketId}} opened (Severity {{Severity}})."
                            }
                        }
                    },
                    new StepBlueprint
                    {
                        StepId = "CheckSeverity",
                        StepType = "Decision",
                        RequiredRoles = new() { "System" },
                        Conditions = new()
                        {
                            { CriticalIncidentCondition, "OnCallEscalation" },
                            { "Default", "L1Review" }
                        },
                        NextSteps = new() { { "Default", "L1Review" } }
                    },
                    new StepBlueprint
                    {
                        StepId = "L1Review",
                        StepType = "HumanTask",
                        RequiredRoles = new() { "Support" },
                        NextSteps = new()
                        {
                            { "EVT-RESOLVE", "Closed" },
                            { "EVT-ESCALATE", "OnCallEscalation" }
                        },
                        Sla = new StepSlaBlueprint
                        {
                            Duration = "4h",
                            TimeoutEvent = "EVT-ESCALATE",
                            EscalationStepId = "OnCallEscalation",
                            EscalationRole = "OnCall",
                            Reminders = new()
                            {
                                new StepReminderBlueprint { Duration = "1h", TriggerEvent = "EVT-SLA-WARN-1H" },
                                new StepReminderBlueprint { Duration = "3h", TriggerEvent = "EVT-SLA-WARN-3H" }
                            }
                        },
                        OnEntry = new()
                        {
                            new StepActionBlueprint
                            {
                                ActionType = "Notification",
                                Target = "SupportInbox",
                                Template = "ALERT: {{TicketId}} assigned to L1 Support. 4h SLA with warnings at 1h and 3h."
                            }
                        }
                    },
                    new StepBlueprint
                    {
                        StepId = "OnCallEscalation",
                        StepType = "HumanTask",
                        RequiredRoles = new() { "OnCall" },
                        NextSteps = new() { { "EVT-CLOSE", "Closed" } },
                        OnEntry = new()
                        {
                            new StepActionBlueprint
                            {
                                ActionType = "Notification",
                                Target = "OnCallPager",
                                Template = "ESCALATION ALERT: {{TicketId}} paged On-Call (Severity {{Severity}})."
                            }
                        }
                    },
                    new StepBlueprint
                    {
                        StepId = "Closed",
                        StepType = "Command",
                        NextSteps = new() { { "Default", "END" } },
                        OnEntry = new()
                        {
                            new StepActionBlueprint
                            {
                                ActionType = "Notification",
                                Target = "IncidentChannel",
                                Template = "Incident {{TicketId}} resolved."
                            }
                        }
                    }
                }
            },
            Roles = IncidentRoles(),
            Capabilities = IncidentCapabilities()
        };

        WorkflowSimulationGovernance.Apply(blueprint);
        return blueprint;
    }

    public static WorkflowClassBlueprint CreateQuoteAutoReviewBlueprint()
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new()
            {
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Quote", Category = EventCategory.Human, AllowedRoles = new() { "Submitter" } },
                new EventBlueprint { EventId = "EVT-ACCEPT", Name = "Accept Quote", Category = EventCategory.Agent, AllowedRoles = new() { "QuoteAgent", "Advisor" } },
                new EventBlueprint { EventId = "EVT-REQUEST-REVISION", Name = "Request Revision", Category = EventCategory.Human, AllowedRoles = new() { "Advisor" } },
                new EventBlueprint { EventId = "EVT-SLA-WARN-4H", Name = "4h quote reminder", Category = EventCategory.System },
                new EventBlueprint { EventId = "EVT-QUOTE-OVERDUE", Name = "Quote Review Overdue", Category = EventCategory.System }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = new() { "Draft", "AgentQueued", "AdvisorQueued", "Accepted", "RevisionRequested", "Overdue" },
                Transitions = new()
                {
                    new TransitionBlueprint { FromState = "Draft", ToState = "AdvisorQueued", EventId = "EVT-SUBMIT", Condition = HighValueQuoteCondition },
                    new TransitionBlueprint { FromState = "Draft", ToState = "AgentQueued", EventId = "EVT-SUBMIT" },
                    new TransitionBlueprint { FromState = "AgentQueued", ToState = "Accepted", EventId = "EVT-ACCEPT" },
                    new TransitionBlueprint { FromState = "AgentQueued", ToState = "RevisionRequested", EventId = "EVT-REQUEST-REVISION" },
                    new TransitionBlueprint { FromState = "AgentQueued", ToState = "Overdue", EventId = "EVT-QUOTE-OVERDUE" },
                    new TransitionBlueprint { FromState = "AdvisorQueued", ToState = "Accepted", EventId = "EVT-ACCEPT" },
                    new TransitionBlueprint { FromState = "AdvisorQueued", ToState = "RevisionRequested", EventId = "EVT-REQUEST-REVISION" },
                    new TransitionBlueprint { FromState = "AdvisorQueued", ToState = "Overdue", EventId = "EVT-QUOTE-OVERDUE" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "SubmitQuote",
                Steps = new()
                {
                    new StepBlueprint
                    {
                        StepId = "SubmitQuote",
                        StepType = "Command",
                        NextSteps = new() { { "EVT-SUBMIT", "CheckAmount" } }
                    },
                    new StepBlueprint
                    {
                        StepId = "CheckAmount",
                        StepType = "Decision",
                        RequiredRoles = new() { "System" },
                        Conditions = new()
                        {
                            { HighValueQuoteCondition, "AdvisorReview" },
                            { "Default", "AgentReview" }
                        },
                        NextSteps = new() { { "Default", "AgentReview" } }
                    },
                    new StepBlueprint
                    {
                        StepId = "AgentReview",
                        StepType = "HumanTask",
                        Actor = StepActor.Agent,
                        RequiredRoles = new() { "QuoteAgent" },
                        DecisionGuideline = "Accept if Amount is at or below 1500 and within 15% of Estimate. Otherwise request revision. Never accept a missing Estimate.",
                        AgentPrompt = "quote-approval",
                        AgentProvider = "quote-llm",
                        AutoCommit = new StepAutoCommitBlueprint
                        {
                            MinConfidence = 0.9,
                            AllowedEvents = new() { "EVT-ACCEPT" }
                        },
                        NextSteps = new()
                        {
                            { "EVT-ACCEPT", "Closed" },
                            { "EVT-REQUEST-REVISION", "Revision" },
                            { "EVT-QUOTE-OVERDUE", "Overdue" }
                        },
                        Sla = new StepSlaBlueprint
                        {
                            Duration = "24h",
                            TimeoutEvent = "EVT-QUOTE-OVERDUE",
                            Reminders = new()
                            {
                                new StepReminderBlueprint { Duration = "4h", TriggerEvent = "EVT-SLA-WARN-4H" }
                            }
                        },
                        OnEntry = new()
                        {
                            new StepActionBlueprint
                            {
                                ActionType = "Notification",
                                Target = "QuoteDesk",
                                Template = "Agent reviewing quote {{QuoteId}} (Amount {{Amount}})."
                            }
                        }
                    },
                    new StepBlueprint
                    {
                        StepId = "AdvisorReview",
                        StepType = "HumanTask",
                        Actor = StepActor.Human,
                        RequiredRoles = new() { "Advisor" },
                        NextSteps = new()
                        {
                            { "EVT-ACCEPT", "Closed" },
                            { "EVT-REQUEST-REVISION", "Revision" },
                            { "EVT-QUOTE-OVERDUE", "Overdue" }
                        },
                        OnEntry = new()
                        {
                            new StepActionBlueprint
                            {
                                ActionType = "Notification",
                                Target = "AdvisorInbox",
                                Template = "Advisor review required for quote {{QuoteId}} (Amount {{Amount}})."
                            }
                        }
                    },
                    new StepBlueprint
                    {
                        StepId = "Closed",
                        StepType = "Command",
                        NextSteps = new() { { "Default", "END" } },
                        OnEntry = new()
                        {
                            new StepActionBlueprint
                            {
                                ActionType = "Notification",
                                Target = "QuoteDesk",
                                Template = "Quote {{QuoteId}} accepted."
                            }
                        }
                    },
                    new StepBlueprint
                    {
                        StepId = "Revision",
                        StepType = "Command",
                        NextSteps = new() { { "Default", "END" } }
                    },
                    new StepBlueprint
                    {
                        StepId = "Overdue",
                        StepType = "Command",
                        NextSteps = new() { { "Default", "END" } }
                    }
                }
            },
            Roles = QuoteRoles(),
            Capabilities = QuoteCapabilities()
        };

        WorkflowSimulationGovernance.Apply(blueprint);
        return blueprint;
    }

    private static WorkflowClassBlueprint CreateExpenseApprovalV2Blueprint()
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new()
            {
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request", AllowedRoles = new() { "Employee", "User" } },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request", AllowedRoles = new() { "Manager" } },
                new EventBlueprint { EventId = "EVT-REJECT", Name = "Reject Request", AllowedRoles = new() { "Manager" } },
                new EventBlueprint { EventId = "EVT-DIRECTOR-APPROVE", Name = "Director Approve", AllowedRoles = new() { "Director" } },
                new EventBlueprint { EventId = "EVT-DIRECTOR-REJECT", Name = "Director Reject", AllowedRoles = new() { "Director" } },
                new EventBlueprint { EventId = "EVT-ESCALATE", Name = "Escalate to Director", AllowedRoles = new() { "Manager" } }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = new() { "Draft", "PendingManager", "PendingDirector", "Approved", "Rejected" },
                Transitions = new()
                {
                    new TransitionBlueprint { FromState = "Draft", ToState = "PendingDirector", EventId = "EVT-SUBMIT", Condition = HighValueExpenseCondition },
                    new TransitionBlueprint { FromState = "Draft", ToState = "PendingManager", EventId = "EVT-SUBMIT" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "Approved", EventId = "EVT-APPROVE" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "PendingDirector", EventId = "EVT-ESCALATE" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "Rejected", EventId = "EVT-REJECT" },
                    new TransitionBlueprint { FromState = "PendingDirector", ToState = "Approved", EventId = "EVT-DIRECTOR-APPROVE" },
                    new TransitionBlueprint { FromState = "PendingDirector", ToState = "Rejected", EventId = "EVT-DIRECTOR-REJECT" }
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
                        NextSteps = new() { { "EVT-SUBMIT", "CheckAmount" } }
                    },
                    new StepBlueprint
                    {
                        StepId = "CheckAmount",
                        StepType = "Decision",
                        RequiredRoles = new() { "System" },
                        Conditions = new()
                        {
                            { HighValueExpenseCondition, "PendingDirector" },
                            { "Default", "PendingManager" }
                        },
                        NextSteps = new() { { "Default", "PendingManager" } }
                    },
                    new StepBlueprint
                    {
                        StepId = "PendingManager",
                        StepType = "HumanTask",
                        RequiredRoles = new() { "Manager" },
                        NextSteps = new()
                        {
                            { "EVT-APPROVE", "Approved" },
                            { "EVT-ESCALATE", "PendingDirector" },
                            { "EVT-REJECT", "Rejected" }
                        }
                    },
                    new StepBlueprint
                    {
                        StepId = "PendingDirector",
                        StepType = "HumanTask",
                        RequiredRoles = new() { "Director" },
                        NextSteps = new()
                        {
                            { "EVT-DIRECTOR-APPROVE", "Approved" },
                            { "EVT-DIRECTOR-REJECT", "Rejected" }
                        }
                    },
                    new StepBlueprint { StepId = "Approved", StepType = "Command", NextSteps = new() { { "Default", "END" } } },
                    new StepBlueprint { StepId = "Rejected", StepType = "Command", NextSteps = new() { { "Default", "END" } } }
                }
            },
            Roles = ExpenseV2Roles(),
            Capabilities = ExpenseV2Capabilities()
        };

        WorkflowSimulationGovernance.Apply(blueprint);
        return blueprint;
    }

    private static WorkflowClassBlueprint CreateExpenseApprovalBlueprint()
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new()
            {
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request", AllowedRoles = new() { "User", "Employee" } },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request", AllowedRoles = new() { "Manager" } },
                new EventBlueprint { EventId = "EVT-REJECT", Name = "Reject Request", AllowedRoles = new() { "Manager" } }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = new() { "Draft", "Pending", "Approved", "Rejected" },
                Transitions = new()
                {
                    new TransitionBlueprint { FromState = "Draft", ToState = "Pending", EventId = "EVT-SUBMIT" },
                    new TransitionBlueprint { FromState = "Pending", ToState = "Approved", EventId = "EVT-APPROVE" },
                    new TransitionBlueprint { FromState = "Pending", ToState = "Rejected", EventId = "EVT-REJECT" }
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
                        RequiredRoles = new() { "User" },
                        NextSteps = new() { { "EVT-SUBMIT", "Pending" } }
                    },
                    new StepBlueprint
                    {
                        StepId = "Pending",
                        StepType = "HumanTask",
                        RequiredRoles = new() { "Manager" },
                        NextSteps = new() { { "EVT-APPROVE", "Approved" }, { "EVT-REJECT", "Rejected" } }
                    },
                    new StepBlueprint { StepId = "Approved", StepType = "Command", NextSteps = new() { { "Default", "END" } } },
                    new StepBlueprint { StepId = "Rejected", StepType = "Command", NextSteps = new() { { "Default", "END" } } }
                }
            },
            Roles = ExpenseRoles(),
            Capabilities = ExpenseCapabilities()
        };

        WorkflowSimulationGovernance.Apply(blueprint);
        return blueprint;
    }

    private static void SetPrivateProperty(object obj, string propName, object value)
    {
        var prop = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(obj, value);
        }
    }
}

