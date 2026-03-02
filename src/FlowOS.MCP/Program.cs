using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Models;
using FlowOS.MCP.Server;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace FlowOS.MCP
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Infrastructure
                    services.AddDbContext<FlowOSDbContext>((serviceProvider, options) =>
                    {
                        var configuration = serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                        var connectionString = configuration.GetConnectionString("DefaultConnection");

                        if (!string.IsNullOrEmpty(connectionString))
                        {
                            options.UseNpgsql(connectionString);
                        }
                        else
                        {
                            options.UseInMemoryDatabase("FlowOS_MCP_Db");
                        }
                    });

                    // Domain Services
                    services.AddScoped<WorkflowClassValidator>();
                    services.AddScoped<WorkflowClassManager>(); // If needed, or use DbContext directly for Drafts
                    
                    // MCP Services
                    services.AddSingleton<IToolRegistry, ToolRegistry>();
                    services.AddSingleton<McpServer>();

                    // Tool Implementations
                    services.AddScoped<GovernanceTools>();
                    services.AddScoped<InfoTools>();
                    services.AddScoped<AnalysisTools>();

                    // Hosted Service to run the MCP Loop
                    services.AddHostedService<McpHostedService>();
                })
                .ConfigureLogging(logging =>
                {
                    // Redirect logging to debug/stderr so it doesn't interfere with stdout JSON-RPC
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
            // Create a scope to resolve tools (though handlers will create their own scope per request ideally)
            // Actually, we can just register delegates that create scopes.
            
            using var scope = _serviceProvider.CreateScope();
            var governance = scope.ServiceProvider.GetRequiredService<GovernanceTools>();
            var info = scope.ServiceProvider.GetRequiredService<InfoTools>();
            var analysis = scope.ServiceProvider.GetRequiredService<AnalysisTools>();

            // We need to wrap the handler to create a scope per request if the tool relies on Scoped services like DbContext
            
            // Info Tools
            _registry.Register("describe_workflowclass_schema", "Get the JSON schema for WorkflowClassBlueprint", null, 
                async (args) => await ExecuteScopedAsync<InfoTools>(t => t.DescribeSchema(args)));

            _registry.Register("list_public_workflowclasses", "List all Public WorkflowClasses", null, 
                async (args) => await ExecuteScopedAsync<InfoTools>(t => t.ListPublic(args)));

            // Analysis Tools
            _registry.Register("explain_validation_violation", "Explain a validation error code and provide hints", null,
                async (args) => await ExecuteScopedAsync<AnalysisTools>(t => t.ExplainValidationViolation(args)));

            _registry.Register("lint_draft_workflowclass", "Analyze a Draft for design quality warnings", null,
                async (args) => await ExecuteScopedAsync<AnalysisTools>(t => t.LintDraftWorkflowClass(args)));

            // Governance Tools
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
}
