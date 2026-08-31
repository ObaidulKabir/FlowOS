using FlowOS.Application.Behaviors;
using FlowOS.Application.Commands.Governance;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Services;
using FlowOS.Domain.Validation;
using FlowOS.Infrastructure;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Engine;
using FlowOS.MCP.Server;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using FlowOS.Security.Interfaces;
using FlowOS.Security.Policies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace FlowOS.MCP;

class Program
{
    static async Task Main(string[] args)
    {
        var transport = Environment.GetEnvironmentVariable("MCP_TRANSPORT") ?? "stdio";
        if (string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase))
            await RunHttpAsync(args);
        else
            await RunStdioAsync(args);
    }

    static async Task RunStdioAsync(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                AddFlowOsMcpServices(services);
                services.AddSingleton<McpServer>();
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

    static async Task RunHttpAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");

        AddFlowOsMcpServices(builder.Services);

        var app = builder.Build();

        ToolRegistration.RegisterAll(
            app.Services.GetRequiredService<IToolRegistry>(),
            app.Services);

        app.Use(async (context, next) =>
        {
            ApplyHttpRequestContext(context);
            await next();
        });

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapMethods("/mcp", new[] { "GET" }, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed));

        app.MapPost("/mcp", async (HttpRequest request, IMcpJsonRpcDispatcher dispatcher, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { error = "Empty body" });

            var outcome = await dispatcher.DispatchAsync(body, ct);
            return outcome.Kind switch
            {
                McpDispatchKind.InvalidJson => Results.BadRequest(new { error = "Invalid JSON-RPC payload" }),
                McpDispatchKind.NoResponse => Results.Accepted(),
                McpDispatchKind.Response => Results.Content(
                    JsonConvert.SerializeObject(outcome.Response, Formatting.None),
                    "application/json"),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        });

        await app.RunAsync();
    }

    static void ApplyHttpRequestContext(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("x-tenant-id", out var tenantHeader)
            && Guid.TryParse(tenantHeader.FirstOrDefault(), out var tenantId))
        {
            McpRequestContext.TenantId = tenantId;
        }

        if (context.Request.Headers.TryGetValue("X-Mock-Role", out var roleHeader))
        {
            var role = roleHeader.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(role))
                McpRequestContext.Role = role;
        }
    }

    static void AddFlowOsMcpServices(IServiceCollection services)
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
        services.AddScoped<IEventRegistry, EventRegistry>();
        services.AddSingleton<WorkflowEngine>();
        services.AddSingleton<StateMachineEngine>();
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
        services.AddSingleton<IMcpJsonRpcDispatcher, McpJsonRpcDispatcher>();

        services.AddScoped<GovernanceTools>();
        services.AddScoped<InfoTools>();
        services.AddScoped<AnalysisTools>();
        services.AddScoped<AgentTools>();
    }
}

public class McpHostedService : BackgroundService
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ToolRegistration.RegisterAll(_registry, _serviceProvider);
        await _server.RunAsync(stoppingToken);
    }
}
