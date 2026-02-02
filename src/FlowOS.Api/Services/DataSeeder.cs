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
using Microsoft.Extensions.Logging;
using FlowOS.Domain.Enums;
using FlowOS.Security.Models; // Add this
using System.Collections.Generic; // Add this

namespace FlowOS.API.Services;

public static class DataSeeder
{
    public static readonly Guid DefaultTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static async Task SeedAsync(FlowOSDbContext context, IServiceProvider serviceProvider, IHostEnvironment env)
    {
        // 1. Ensure Tenant
        if (!await context.Tenants.AnyAsync())
        {
            var tenant = new Tenant("Default Tenant");
            SetPrivateProperty(tenant, "TenantId", DefaultTenantId);
            context.Tenants.Add(tenant);
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
            adminRole.AddPermission("role.create"); // Just in case
            adminRole.AddPermission("agent.insight.publish"); // For notifications
            
            context.Roles.Add(adminRole);
            await context.SaveChangesAsync();
        }

        // 2. Load Configuration (Dev Only)
        if (env.IsDevelopment())
        {
            // Locate config folder relative to execution
            var potentialPaths = new[] 
            {
                Path.Combine(Directory.GetCurrentDirectory(), "flowos-config"), // If running from root
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "flowos-config"), // If running from src/FlowOS.API
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "flowos-config") // From bin
            };
            
            string configRoot = null;
            foreach (var path in potentialPaths)
            {
                if (Directory.Exists(path))
                {
                    configRoot = path;
                    break;
                }
            }

            if (configRoot != null && Directory.Exists(configRoot))
            {
                var logger = serviceProvider.GetRequiredService<ILogger<ConfigurationLoader>>();
                var loader = new ConfigurationLoader(context, logger, configRoot);
                await loader.LoadAllAsync(DefaultTenantId);
            }
            else 
            {
                 var logger = serviceProvider.GetRequiredService<ILogger<ConfigurationLoader>>();
                 logger.LogWarning("Could not find flowos-config directory. Tried: {Paths}", string.Join(", ", potentialPaths));
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

            var demoWc = new WorkflowClass(clientTenantId, "ExpenseApproval", "1.0.0", demoBp);
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
            publicWc.Publish();
            publicWc.SubmitForReview();
            publicWc.ApproveAsPublic();
            context.WorkflowClasses.Add(publicWc);

            await context.SaveChangesAsync();
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

