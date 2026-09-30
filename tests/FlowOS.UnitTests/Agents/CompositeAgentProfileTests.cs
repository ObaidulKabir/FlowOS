using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Agents.Abstractions;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Agents;

public class CompositeAgentProfileTests
{
    [Fact]
    public async Task DecisionPacketBuilder_ResolvesProfile_ByAgentProviderAlias()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"agent-profile-test-{Guid.NewGuid()}")
            .Options;

        await using var db = new FlowOSDbContext(options);
        var registry = new PluginBindingRegistryService(db);

        // 1. Register an LLM provider binding
        await registry.UpsertAsync(
            tenantId,
            PluginBindingTypes.Agent,
            "tenant-openai",
            AgentProviderKinds.OpenAi,
            true,
            JsonSerializer.Serialize(new AgentProviderConfiguration
            {
                ApiKey = "sk-test-12345",
                Model = "gpt-4o"
            }));

        // 2. Register a Prompt binding
        await registry.UpsertAsync(
            tenantId,
            PluginBindingTypes.Prompt,
            "credit-underwriting-prompt",
            AgentPromptKinds.Markdown,
            true,
            JsonSerializer.Serialize(new AgentPromptConfiguration
            {
                Title = "Credit Evaluation",
                System = "You are a professional credit underwriter.",
                Instructions = "Evaluate applicant credit score and debt-to-income ratio."
            }));

        // 3. Register a resource tool binding (business capability -> plugin)
        await registry.UpsertAsync(
            tenantId,
            PluginBindingTypes.Action,
            "crm.customer.get.v1",
            "LookupRecord",
            true);

        // 4. Register composite Agent Profile
        var profileConfig = new AgentProfileConfiguration
        {
            Role = "LoanOfficer",
            Description = "Senior underwriter for personal loans",
            ProviderAlias = "tenant-openai",
            PromptAlias = "credit-underwriting-prompt",
            ToolAliases = new List<string> { "crm.customer.get.v1", "CalculateRiskRatio" },
            AutoCommitThreshold = 0.90,
            AllowedEvents = new List<string> { "ApproveLoan", "RejectLoan" }
        };

        await registry.UpsertAsync(
            tenantId,
            PluginBindingTypes.Profile,
            "CreditUnderwriter",
            "LoanOfficer",
            true,
            JsonSerializer.Serialize(profileConfig));

        // 5. Seed a WorkflowClass blueprint referencing the agent profile alias
        var blueprint = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Underwrite",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "Underwrite",
                        StepType = "Decision",
                        Actor = "Agent",
                        AgentProvider = "CreditUnderwriter", // Matches agent profile alias!
                        NextSteps = new Dictionary<string, string>
                        {
                            ["ApproveLoan"] = "Disburse",
                            ["RejectLoan"] = "Notify"
                        }
                    }
                }
            }
        };

        var workflowClass = new WorkflowClass(tenantId, "LoanApproval", "1.0.0", blueprint);
        db.WorkflowClasses.Add(workflowClass);
        await db.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(db);
        var builder = new DecisionPacketBuilder(unitOfWork, registry);

        // Act
        var packet = await builder.PreviewAsync(tenantId, workflowClass.Id, "Underwrite");

        // Assert
        Assert.NotNull(packet);
        // Provider hydrated from profile's ProviderAlias:
        Assert.NotNull(packet!.Provider);
        Assert.Equal("tenant-openai", packet.Provider!.Alias);
        Assert.Equal(AgentProviderKinds.OpenAi, packet.Provider.ProviderName);
        Assert.Equal("gpt-4o", packet.Provider.Model);
        Assert.True(packet.Provider.HasApiKey);

        // Prompt hydrated from profile's PromptAlias:
        Assert.NotNull(packet.PromptBinding);
        Assert.Equal("credit-underwriting-prompt", packet.PromptBinding!.Alias);
        Assert.Equal("Credit Evaluation", packet.PromptBinding.Title);
        Assert.Equal("Evaluate applicant credit score and debt-to-income ratio.", packet.Prompt.Instructions);

        // Curated Tools hydrated from profile's ToolAliases:
        Assert.NotNull(packet.Tools);
        Assert.Contains(packet.Tools, t => t.Name == "crm.customer.get.v1" && t.Kind == "plugin" && t.Provider == "LookupRecord");
        Assert.Contains(packet.Tools, t => t.Name == "CalculateRiskRatio");

        // Autonomy policy hydrated from profile:
        Assert.NotNull(packet.AutoCommit);
        Assert.Equal(0.90, packet.AutoCommit!.MinConfidence);
        Assert.Contains("ApproveLoan", packet.AutoCommit.AllowedEvents);
    }

    [Fact]
    public async Task DecisionPacketBuilder_ResolvesProfile_ByAllowedRole_WhenProviderUnspecified()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"agent-role-test-{Guid.NewGuid()}")
            .Options;

        await using var db = new FlowOSDbContext(options);
        var registry = new PluginBindingRegistryService(db);

        await registry.UpsertAsync(
            tenantId,
            PluginBindingTypes.Agent,
            "fast-claude",
            AgentProviderKinds.Anthropic,
            true,
            JsonSerializer.Serialize(new AgentProviderConfiguration
            {
                ApiKey = "sk-ant-test",
                Model = "claude-3-5-sonnet"
            }));

        // Register profile with role "TriageOfficer"
        var profileConfig = new AgentProfileConfiguration
        {
            Role = "TriageOfficer",
            ProviderAlias = "fast-claude",
            AutoCommitThreshold = 0.80
        };

        await registry.UpsertAsync(
            tenantId,
            PluginBindingTypes.Profile,
            "HelpdeskAgent",
            "TriageOfficer",
            true,
            JsonSerializer.Serialize(profileConfig));

        var blueprint = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "TriageStep",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "TriageStep",
                        StepType = "Decision",
                        Actor = "Agent",
                        AllowedRoles = new List<string> { "TriageOfficer" }, // Matches profile's Role!
                        NextSteps = new Dictionary<string, string>
                        {
                            ["Escalate"] = "Level2",
                            ["Resolve"] = "Done"
                        }
                    }
                }
            }
        };

        var workflowClass = new WorkflowClass(tenantId, "SupportWorkflow", "1.0.0", blueprint);
        db.WorkflowClasses.Add(workflowClass);
        await db.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(db);
        var builder = new DecisionPacketBuilder(unitOfWork, registry);

        // Act
        var packet = await builder.PreviewAsync(tenantId, workflowClass.Id, "TriageStep");

        // Assert
        Assert.NotNull(packet);
        Assert.NotNull(packet!.Provider);
        Assert.Equal("fast-claude", packet.Provider!.Alias);
        Assert.Equal(AgentProviderKinds.Anthropic, packet.Provider.ProviderName);
        Assert.NotNull(packet.AutoCommit);
        Assert.Equal(0.80, packet.AutoCommit!.MinConfidence);
    }
}
