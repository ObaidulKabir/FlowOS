using FlowOS.Application.Behaviors;
using FlowOS.Application.Commands.Governance;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Services;
using FlowOS.Domain.Validation;
using FlowOS.Infrastructure;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.MCP.Models;
using FlowOS.MCP.Server;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using FlowOS.Security.Interfaces;
using FlowOS.Security.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowOS.MCP;

class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddDbContext<FlowOSDbContext>((serviceProvider, options) =>
                {
                    var configuration = serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                    var connectionString = configuration["ConnectionStrings:DefaultConnection"];

                    if (!string.IsNullOrEmpty(connectionString))
                        options.UseNpgsql(connectionString);
                    else
                        options.UseInMemoryDatabase("FlowOS_MCP_Db");
                });

                services.AddFlowOSPersistence();
                services.AddMemoryCache();
                services.AddScoped<ICurrentUser, McpCurrentUser>();
                services.AddScoped<ICapabilityService, CapabilityService>();
                services.AddScoped<IPolicyProvider, EfCorePolicyProvider>();
                services.AddScoped<IPolicyEvaluator, DefaultPolicyEvaluator>();
                services.AddScoped<WorkflowClassValidator>();
                services.AddScoped<IWorkflowJsonLinter, WorkflowJsonLinter>();
                services.AddScoped<IWorkflowClassManager, WorkflowClassManager>();
                services.AddScoped<IWorkflowClassVersionManager, WorkflowClassVersionManager>();

                services.AddMediatR(cfg =>
                {
                    cfg.RegisterServicesFromAssembly(typeof(CreateWorkflowClassCommand).Assembly);
                    cfg.AddOpenBehavior(typeof(PolicyEnforcementBehavior<,>));
                });

                services.AddSingleton<IToolRegistry, ToolRegistry>();
                services.AddSingleton<McpServer>();

                services.AddScoped<GovernanceTools>();
                services.AddScoped<InfoTools>();
                services.AddScoped<AnalysisTools>();
                services.AddScoped<AgentTools>();

                services.AddHostedService<McpHostedService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            })
            .Build();

        await host.RunAsync();
    }
}

public class McpHostedService : IHostedService
{
    private readonly McpServer _server;
    private readonly IToolRegistry _registry;
    private readonly IServiceProvider _serviceProvider;

    public McpHostedService(McpServer server, IToolRegistry registry, IServiceProvider serviceProvider)
    {
        _server = server;
        _registry = registry;
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        RegisterTools();
        await _server.RunAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void RegisterTools()
    {
        _registry.Register("describe_workflowclass_schema", "Get the JSON schema for WorkflowClassBlueprint", null,
            async (args) => await ExecuteScopedAsync<InfoTools>(t => t.DescribeSchema(args)));

        _registry.Register("list_public_workflowclasses", "List all Public WorkflowClasses", null,
            async (args) => await ExecuteScopedAsync<InfoTools>(t => t.ListPublic(args)));

        _registry.Register("list_available_agents", "List all available AI Agents and their capabilities", null,
            async (args) => await ExecuteScopedAsync<AgentTools>(t => t.ListAvailableAgents(args)));

        _registry.Register("suggest_agent_action", "Get suggested actions from an agent for a workflow instance", null,
            async (args) => await ExecuteScopedAsync<AgentTools>(t => t.SuggestAgentAction(args)));

        _registry.Register("explain_validation_violation", "Explain a validation error code and provide hints", null,
            async (args) => await ExecuteScopedAsync<AnalysisTools>(t => t.ExplainValidationViolation(args)));

        _registry.Register("lint_draft_workflowclass", "Analyze a Draft for design quality warnings", null,
            async (args) => await ExecuteScopedAsync<AnalysisTools>(t => t.LintDraftWorkflowClass(args)));

        _registry.Register("create_draft_workflowclass", "Create a new Draft WorkflowClass", null,
            async (args) => await ExecuteScopedAsync<GovernanceTools>(t => t.CreateDraft(args)));

        _registry.Register("update_draft_workflowclass", "Update a Draft WorkflowClass", null,
            async (args) => await ExecuteScopedAsync<GovernanceTools>(t => t.UpdateDraft(args)));

        _registry.Register("validate_draft_workflowclass", "Validate a Draft WorkflowClass", null,
            async (args) => await ExecuteScopedAsync<GovernanceTools>(t => t.ValidateDraft(args)));

        _registry.Register("fork_public_workflowclass", "Fork a Public WorkflowClass to a new Draft", null,
            async (args) => await ExecuteScopedAsync<GovernanceTools>(t => t.ForkPublic(args)));
    }

    private async Task<CallToolResult> ExecuteScopedAsync<T>(Func<T, Task<CallToolResult>> action) where T : notnull
    {
        using var scope = _serviceProvider.CreateScope();
        var tool = scope.ServiceProvider.GetRequiredService<T>();
        return await action(tool);
    }
}
