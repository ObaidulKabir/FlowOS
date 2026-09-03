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
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        var demoClientTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        if (!await context.Tenants.AnyAsync(t => t.TenantId == demoClientTenantId))
        {
            var clientTenant = new Tenant("Demo Client Tenant");
            SetPrivateProperty(clientTenant, "TenantId", demoClientTenantId);
            context.Tenants.Add(clientTenant);
            await context.SaveChangesAsync();
        }

        // 1.2 Ensure Demo Tenant API Keys
        if (!await context.TenantApiKeys.AnyAsync(k => k.TenantId == demoClientTenantId))
        {
            var demoKey1 = new TenantApiKey(demoClientTenantId, "Production Key", "flowos_prod_secret_key_32_chars_min", "ERP Integration", "Production", new[] { "*" });
            var demoKey2 = new TenantApiKey(demoClientTenantId, "Local Dev Key", "local-development-key-change-me", "Developer Sandbox", "Development", new[] { "*" });
            context.TenantApiKeys.AddRange(demoKey1, demoKey2);
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
            var demoBp = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
            {
                Events = new() 
                { 
                    new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request" },
                    new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request" }
                },
                StateMachine = new FlowOS.Domain.Blueprints.StateMachineBlueprint
                {
                    InitialState = "Draft",
                    States = new() { "Draft", "Pending", "Approved" },
                    Transitions = new() 
                    {
                        new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "Draft", ToState = "Pending", EventId = "EVT-SUBMIT" },
                        new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "Pending", ToState = "Approved", EventId = "EVT-APPROVE" }
                    }
                },
                Workflow = new FlowOS.Domain.Blueprints.WorkflowBlueprint
                {
                    StartStepId = "Draft",
                    Steps = new() 
                    {
                        new FlowOS.Domain.Blueprints.StepBlueprint 
                        { 
                            StepId = "Draft", 
                            StepType = "Command",
                            NextSteps = new() { { "EVT-SUBMIT", "Pending" } }
                        },
                        new FlowOS.Domain.Blueprints.StepBlueprint 
                        { 
                            StepId = "Pending", 
                            StepType = "HumanTask",
                            NextSteps = new() { { "EVT-APPROVE", "Approved" } }
                        },
                        new FlowOS.Domain.Blueprints.StepBlueprint 
                        { 
                            StepId = "Approved", 
                            StepType = "Command",
                            NextSteps = new() { { "Default", "END" } }
                        }
                    }
                }
            };

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
                // Force update blueprint to ensure validity (in case DB has stale invalid JSON)
                var demoBpFix = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
                {
                    Events = new() 
                    { 
                        new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request" },
                        new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request" },
                        new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-REJECT", Name = "Reject Request" }
                    },
                    StateMachine = new FlowOS.Domain.Blueprints.StateMachineBlueprint
                    {
                        InitialState = "Draft",
                        States = new() { "Draft", "Pending", "Approved", "Rejected" },
                        Transitions = new() 
                        {
                            new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "Draft", ToState = "Pending", EventId = "EVT-SUBMIT" },
                            new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "Pending", ToState = "Approved", EventId = "EVT-APPROVE" },
                            new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "Pending", ToState = "Rejected", EventId = "EVT-REJECT" }
                        }
                    },
                    Workflow = new FlowOS.Domain.Blueprints.WorkflowBlueprint
                    {
                        StartStepId = "Draft",
                        Steps = new() 
                        {
                            new FlowOS.Domain.Blueprints.StepBlueprint 
                            { 
                                StepId = "Draft", 
                                StepType = "Command",
                                NextSteps = new() { { "EVT-SUBMIT", "Pending" } }
                            },
                            new FlowOS.Domain.Blueprints.StepBlueprint 
                            { 
                                StepId = "Pending", 
                                StepType = "HumanTask",
                                NextSteps = new() { { "EVT-APPROVE", "Approved" }, { "EVT-REJECT", "Rejected" } }
                            },
                            new FlowOS.Domain.Blueprints.StepBlueprint 
                            { 
                                StepId = "Approved", 
                                StepType = "Command",
                                NextSteps = new() { { "Default", "END" } }
                            },
                            new FlowOS.Domain.Blueprints.StepBlueprint 
                            { 
                                StepId = "Rejected", 
                                StepType = "Command",
                                NextSteps = new() { { "Default", "END" } }
                            }
                        }
                    }
                };
                
                SetPrivateProperty(wc, "Definition", demoBpFix);
                if (wc.Status != WorkflowClassStatus.Published)
                {
                    var manager = new WorkflowClassManager();
                    manager.Publish(wc);
                }
                
                // Manually Create WorkflowDefinition (Bridging the gap between Governance and Engine)
                // In a real app, a Domain Event Handler for WorkflowClassPublished would do this.
                var def = new WorkflowDefinition(wc.TenantId, wc.Name, 1, demoBpFix.Workflow.StartStepId);
                // Map Steps
                foreach (var stepBp in demoBpFix.Workflow.Steps)
                {
                    var stepType = Enum.Parse<WorkflowStepType>(stepBp.StepType);
                    var stepDef = new WorkflowStepDefinition(stepBp.StepId, stepType);
                    foreach (var next in stepBp.NextSteps)
                    {
                        stepDef.NextSteps.Add(next.Key, next.Value);
                    }
                    def.AddStep(stepDef);
                }
                
                def.Publish();
                
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
                    
                    // Re-add steps
                    // Force update blueprint to ensure validity (in case DB has stale invalid JSON)
                    var demoBpFix = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
                    {
                        Events = new() 
                        { 
                            new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request" },
                            new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request" },
                            new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-REJECT", Name = "Reject Request" }
                        },
                        StateMachine = new FlowOS.Domain.Blueprints.StateMachineBlueprint
                        {
                            InitialState = "Draft",
                            States = new() { "Draft", "Pending", "Approved", "Rejected" },
                            Transitions = new() 
                            {
                                new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "Draft", ToState = "Pending", EventId = "EVT-SUBMIT" },
                                new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "Pending", ToState = "Approved", EventId = "EVT-APPROVE" },
                                new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "Pending", ToState = "Rejected", EventId = "EVT-REJECT" }
                            }
                        },
                        Workflow = new FlowOS.Domain.Blueprints.WorkflowBlueprint
                        {
                            StartStepId = "Draft",
                            Steps = new() 
                            {
                                new FlowOS.Domain.Blueprints.StepBlueprint 
                                { 
                                    StepId = "Draft", 
                                    StepType = "Command",
                                    NextSteps = new() { { "EVT-SUBMIT", "Pending" } }
                                },
                                new FlowOS.Domain.Blueprints.StepBlueprint 
                                { 
                                    StepId = "Pending", 
                                    StepType = "HumanTask",
                                    NextSteps = new() { { "EVT-APPROVE", "Approved" }, { "EVT-REJECT", "Rejected" } }
                                },
                                new FlowOS.Domain.Blueprints.StepBlueprint 
                                { 
                                    StepId = "Approved", 
                                    StepType = "Command",
                                    NextSteps = new() { { "Default", "END" } }
                                },
                                new FlowOS.Domain.Blueprints.StepBlueprint 
                                { 
                                    StepId = "Rejected", 
                                    StepType = "Command",
                                    NextSteps = new() { { "Default", "END" } }
                                }
                            }
                        }
                    };

                    foreach (var stepBp in demoBpFix.Workflow.Steps)
                    {
                        var stepType = Enum.Parse<WorkflowStepType>(stepBp.StepType);
                        var stepDef = new WorkflowStepDefinition(stepBp.StepId, stepType);
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
        var v2Bp = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
        {
            Events = new() 
            { 
                new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request" },
                new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request" },
                new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-REJECT", Name = "Reject Request" },
                new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-DIRECTOR-APPROVE", Name = "Director Approve" },
                new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-DIRECTOR-REJECT", Name = "Director Reject" },
                new FlowOS.Domain.Blueprints.EventBlueprint { EventId = "EVT-ESCALATE", Name = "Escalate to Director" }
            },
            StateMachine = new FlowOS.Domain.Blueprints.StateMachineBlueprint
            {
                InitialState = "Draft",
                States = new() { "Draft", "PendingManager", "PendingDirector", "Approved", "Rejected" },
                Transitions = new() 
                {
                    new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "Draft", ToState = "PendingManager", EventId = "EVT-SUBMIT" },
                    // Manager Approval Logic
                    new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "PendingManager", ToState = "Approved", EventId = "EVT-APPROVE" }, // < $100
                    new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "PendingManager", ToState = "PendingDirector", EventId = "EVT-ESCALATE" }, // > $100
                    new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "PendingManager", ToState = "Rejected", EventId = "EVT-REJECT" },
                    // Director Approval Logic
                    new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "PendingDirector", ToState = "Approved", EventId = "EVT-DIRECTOR-APPROVE" },
                    new FlowOS.Domain.Blueprints.TransitionBlueprint { FromState = "PendingDirector", ToState = "Rejected", EventId = "EVT-DIRECTOR-REJECT" }
                }
            },
            Workflow = new FlowOS.Domain.Blueprints.WorkflowBlueprint
            {
                StartStepId = "Draft",
                Steps = new() 
                {
                    new FlowOS.Domain.Blueprints.StepBlueprint 
                    { 
                        StepId = "Draft", 
                        StepType = "Command",
                        NextSteps = new() { { "EVT-SUBMIT", "CheckAmount" } } // Go to System Check first
                    },
                    new FlowOS.Domain.Blueprints.StepBlueprint 
                    { 
                        StepId = "CheckAmount", 
                        StepType = "SystemTask", // This needs to be supported or simulated
                        // For now, let's simplify: Submit goes to PendingManager.
                        // We will handle the condition in the Manager Step logic or via specialized events?
                        // FlowOS currently supports explicit transitions. 
                        // Let's implement it as:
                        // Draft -> (EVT-SUBMIT) -> PendingManager
                        // PendingManager -> (EVT-APPROVE) -> CheckDirectorNeeded (System Step?)
                        // If we don't have System Steps with logic yet, we can simulate it in the client/backend?
                        // "require approval of director if amount >$100"
                        // This implies the Manager approves, and THEN it goes to Director if high value.
                        // OR it goes straight to Director? Usually Manager first.
                        
                        // Let's try this flow:
                        // 1. Submit -> PendingManager
                        // 2. Manager Approves (EVT-APPROVE)
                        // 3. Workflow Engine checks condition? (Not yet implemented in engine)
                        // 4. So we need a "Gateway" step or the Client decides which event to fire?
                        // The user asked to "create another version... that require approval".
                        // Let's model it with explicit steps for now.
                        
                        // Revised Flow:
                        // Draft -> PendingManager
                        // PendingManager -> (EVT-APPROVE) -> CheckHighValue (System)
                        // CheckHighValue -> (EVT-HIGH-VALUE) -> PendingDirector
                        // CheckHighValue -> (EVT-LOW-VALUE) -> Approved
                        
                        // Since we don't have automatic system tasks yet in this seed, we'll rely on the backend to fire the correct event based on amount.
                        // Backend will fire EVT-APPROVE-LOW (<100) or EVT-APPROVE-HIGH (>100).
                        
                        NextSteps = new() { { "EVT-SUBMIT", "PendingManager" } }
                    },
                    new FlowOS.Domain.Blueprints.StepBlueprint 
                    { 
                        StepId = "PendingManager", 
                        StepType = "HumanTask",
                        NextSteps = new() 
                        { 
                            { "EVT-APPROVE", "Approved" }, // < 100
                            { "EVT-ESCALATE", "PendingDirector" }, // > 100
                            { "EVT-REJECT", "Rejected" } 
                        }
                    },
                    new FlowOS.Domain.Blueprints.StepBlueprint 
                    { 
                        StepId = "PendingDirector", 
                        StepType = "HumanTask",
                        NextSteps = new() 
                        { 
                            { "EVT-DIRECTOR-APPROVE", "Approved" }, 
                            { "EVT-DIRECTOR-REJECT", "Rejected" } 
                        }
                    },
                    new FlowOS.Domain.Blueprints.StepBlueprint { StepId = "Approved", StepType = "Command", NextSteps = new() { { "Default", "END" } } },
                    new FlowOS.Domain.Blueprints.StepBlueprint { StepId = "Rejected", StepType = "Command", NextSteps = new() { { "Default", "END" } } }
                }
            }
        };

        var v2Wc = new WorkflowClass(clientTenantId, v2Name, "1.0.0", v2Bp);
        SetPrivateProperty(v2Wc, "Id", Guid.Parse("e912ab44-2222-2222-2222-222222222222"));
        var manager2 = new WorkflowClassManager();
        manager2.Publish(v2Wc);
        context.WorkflowClasses.Add(v2Wc);
        
        // Create Definition
        var def = new WorkflowDefinition(clientTenantId, v2Name, 1, "Draft");
        
        // Draft
        var draft = new WorkflowStepDefinition("Draft", WorkflowStepType.Command);
        draft.NextSteps.Add("EVT-SUBMIT", "PendingManager");
        def.AddStep(draft);

        // PendingManager
        var mgr = new WorkflowStepDefinition("PendingManager", WorkflowStepType.HumanTask);
        mgr.NextSteps.Add("EVT-APPROVE", "Approved"); // Low value path
        mgr.NextSteps.Add("EVT-ESCALATE", "PendingDirector"); // High value path
        mgr.NextSteps.Add("EVT-REJECT", "Rejected");
        def.AddStep(mgr);

        // PendingDirector
        var dir = new WorkflowStepDefinition("PendingDirector", WorkflowStepType.HumanTask);
        dir.NextSteps.Add("EVT-DIRECTOR-APPROVE", "Approved");
        dir.NextSteps.Add("EVT-DIRECTOR-REJECT", "Rejected");
        def.AddStep(dir);

        // End States
        var approved = new WorkflowStepDefinition("Approved", WorkflowStepType.Command);
        approved.NextSteps.Add("Default", "END");
        def.AddStep(approved);

        var rejected = new WorkflowStepDefinition("Rejected", WorkflowStepType.Command);
        rejected.NextSteps.Add("Default", "END");
        def.AddStep(rejected);

        def.Publish();
        context.WorkflowDefinitions.Add(def);
        await context.SaveChangesAsync();
    }
    
    // 5. Ensure Admin Role for Client Tenant (to allow start workflow)
    if (!await context.Roles.AnyAsync(r => r.Name == "Admin" && r.TenantId == clientTenantId))
    {
        var adminRole = new Role(clientTenantId, "Admin");
        adminRole.AddPermission("workflow.start");
        adminRole.AddPermission("workflow.create");
        adminRole.AddPermission("workflow.read");
        adminRole.AddPermission("event.publish");
        adminRole.AddPermission("task.complete");
        adminRole.AddPermission("workflow.approve_public");
        context.Roles.Add(adminRole);
    }
    
    // 5.1. Seed Employee Role
    if (!await context.Roles.AnyAsync(r => r.Name == "Employee" && r.TenantId == clientTenantId))
    {
        var empRole = new Role(clientTenantId, "Employee");
        empRole.AddPermission("workflow.start"); // Can start workflow
        empRole.AddPermission("workflow.read"); // Can view their workflows
        empRole.AddPermission("event.publish.EVT-SUBMIT"); // Can only submit
        context.Roles.Add(empRole);
    }

    // 5.2. Seed Manager Role
    if (!await context.Roles.AnyAsync(r => r.Name == "Manager" && r.TenantId == clientTenantId))
    {
        var mgrRole = new Role(clientTenantId, "Manager");
        mgrRole.AddPermission("workflow.read");
        mgrRole.AddPermission("event.publish.EVT-APPROVE"); // Can approve standard
        mgrRole.AddPermission("event.publish.EVT-REJECT"); // Can reject
        mgrRole.AddPermission("event.publish.EVT-ESCALATE"); // Can escalate
        context.Roles.Add(mgrRole);
    }
    else
    {
        // Update existing Manager role if it's missing EVT-ESCALATE
        // Note: Permissions is loaded as a Value Object/Owned Type in EF, or simple collection depending on config.
        // But Role.Permissions is HashSet<string>.
        // EF Core loading of owned types/collections might need explicit Include if it's a separate table.
        // Assuming simple string collection for now.
        
        var existingMgr = await context.Roles
            .FirstOrDefaultAsync(r => r.Name == "Manager" && r.TenantId == clientTenantId);
            
        if (existingMgr != null)
        {
            // Force load permissions if they are not loaded (though usually they are with the entity if configured as owned)
            // But if it's a separate table, we might need to load it.
            // context.Entry(existingMgr).Collection(r => r.Permissions).Load(); 
            // However, Role.Permissions is a HashSet<string> which EF maps to a table usually.
            
            // Re-fetch with explicit include if needed, but above we used Include(r => r.Permissions) which failed because string doesn't have properties.
            // Role.Permissions is ICollection<string>? No, it's HashSet<string>.
            
            // Let's just try to add. The AddPermission method checks for duplicates internally.
            
            existingMgr.AddPermission("event.publish.EVT-ESCALATE");
            existingMgr.AddPermission("event.publish.EVT-APPROVE");
            existingMgr.AddPermission("event.publish.EVT-REJECT");
            existingMgr.AddPermission("workflow.read");
            
            context.Roles.Update(existingMgr);
        }
    }

    // 5.3. Seed Director Role
    if (!await context.Roles.AnyAsync(r => r.Name == "Director" && r.TenantId == clientTenantId))
    {
        var dirRole = new Role(clientTenantId, "Director");
        dirRole.AddPermission("workflow.read");
        dirRole.AddPermission("event.publish.EVT-DIRECTOR-APPROVE"); // Can approve escalated
        dirRole.AddPermission("event.publish.EVT-DIRECTOR-REJECT"); // Can reject escalated
        context.Roles.Add(dirRole);
    }
    
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

