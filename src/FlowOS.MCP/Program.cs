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
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FlowOS.MCP;

public partial class Program
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
        var app = BuildHttpApp(args);
        await app.RunAsync();
    }

    public static WebApplication BuildHttpApp(
        string[] args,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");

        AddFlowOsMcpServices(builder.Services);
        configureBuilder?.Invoke(builder);

        var apiKey = builder.Configuration["MCP_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("MCP_API_KEY must be configured for HTTP transport.");

        var serviceRole = builder.Configuration["MCP_ROLE"];
        if (string.IsNullOrWhiteSpace(serviceRole))
            throw new InvalidOperationException("MCP_ROLE must be configured for HTTP transport.");

        var allowedOrigins = (builder.Configuration["MCP_ALLOWED_ORIGINS"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var app = builder.Build();

        ToolRegistration.RegisterAll(
            app.Services.GetRequiredService<IToolRegistry>(),
            app.Services);

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.Equals("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var origin = context.Request.Headers.Origin.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(origin) && !allowedOrigins.Contains(origin))
            {
                await WriteHttpError(context, StatusCodes.Status403Forbidden, -32003, "Origin is not allowed.");
                return;
            }

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                if (!string.IsNullOrWhiteSpace(origin))
                {
                    context.Response.Headers.AccessControlAllowOrigin = origin;
                    context.Response.Headers.AccessControlAllowMethods = "POST, GET, OPTIONS";
                    context.Response.Headers.AccessControlAllowHeaders =
                        "Content-Type, Accept, MCP-Protocol-Version, X-MCP-API-Key, x-tenant-id";
                    context.Response.Headers.Vary = "Origin";
                }
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            var suppliedApiKey = context.Request.Headers["X-MCP-API-Key"].FirstOrDefault();
            if (!FixedTimeEquals(apiKey, suppliedApiKey))
            {
                context.Response.Headers.WWWAuthenticate = "ApiKey";
                await WriteHttpError(context, StatusCodes.Status401Unauthorized, -32001, "Authentication required.");
                return;
            }

            var tenantText = context.Request.Headers["x-tenant-id"].FirstOrDefault();
            if (!Guid.TryParse(tenantText, out var tenantId) || tenantId == Guid.Empty)
            {
                await WriteHttpError(context, StatusCodes.Status400BadRequest, -32602, "A valid x-tenant-id header is required.");
                return;
            }

            McpRequestContext.TenantId = tenantId;
            McpRequestContext.Role = serviceRole;
            McpRequestContext.IsAuthenticatedTransport = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(origin))
                {
                    context.Response.Headers.AccessControlAllowOrigin = origin;
                    context.Response.Headers.Vary = "Origin";
                }
                await next();
            }
            finally
            {
                McpRequestContext.Clear();
            }
        });

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/mcp", (HttpContext context) =>
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Append("Allow", "POST");
            return Task.CompletedTask;
        });

        app.MapPost("/mcp", async (HttpRequest request, IMcpJsonRpcDispatcher dispatcher, CancellationToken ct) =>
        {
            if (!request.HasJsonContentType())
                return JsonRpcHttpError(StatusCodes.Status415UnsupportedMediaType, -32600, "Content-Type must be application/json.");

            var accepts = request.GetTypedHeaders().Accept;
            if (accepts == null
                || !accepts.Any(value => value.MediaType.Value == "application/json")
                || !accepts.Any(value => value.MediaType.Value == "text/event-stream"))
            {
                return JsonRpcHttpError(StatusCodes.Status406NotAcceptable, -32600,
                    "Accept must include application/json and text/event-stream.");
            }

            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return JsonRpcHttpError(StatusCodes.Status400BadRequest, -32700, "Parse error");

            if (IsInitializeRequest(body) == false)
            {
                var protocolVersion = request.Headers["MCP-Protocol-Version"].FirstOrDefault();
                if (protocolVersion != McpJsonRpcDispatcher.SupportedProtocolVersion)
                {
                    return JsonRpcHttpError(StatusCodes.Status400BadRequest, -32602,
                        $"MCP-Protocol-Version must be {McpJsonRpcDispatcher.SupportedProtocolVersion}.");
                }
            }

            var outcome = await dispatcher.DispatchAsync(body, ct);
            return outcome.Kind switch
            {
                McpDispatchKind.NoResponse => Results.Accepted(),
                McpDispatchKind.Response => Results.Content(
                    JsonConvert.SerializeObject(outcome.Response, Formatting.None),
                    "application/json",
                    statusCode: ResponseStatusCode(outcome.Response)),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        });

        return app;
    }

    private static bool FixedTimeEquals(string expected, string? supplied)
    {
        if (string.IsNullOrEmpty(supplied)) return false;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    private static bool? IsInitializeRequest(string body)
    {
        try
        {
            var token = JToken.Parse(body);
            return token is JObject obj && obj["method"]?.ToString() == "initialize";
        }
        catch { return null; }
    }

    private static int ResponseStatusCode(object? response) =>
        response is FlowOS.MCP.Models.JsonRpcResponse
        {
            Error.Code: -32700 or -32600
        }
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status200OK;

    private static IResult JsonRpcHttpError(int statusCode, int code, string message) =>
        Results.Json(new FlowOS.MCP.Models.JsonRpcResponse
        {
            Id = null,
            Error = new FlowOS.MCP.Models.JsonRpcError { Code = code, Message = message }
        }, statusCode: statusCode);

    private static async Task WriteHttpError(HttpContext context, int statusCode, int code, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonConvert.SerializeObject(new FlowOS.MCP.Models.JsonRpcResponse
        {
            Id = null,
            Error = new FlowOS.MCP.Models.JsonRpcError { Code = code, Message = message }
        }));
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
        McpRequestContext.Role = Environment.GetEnvironmentVariable("MCP_ROLE") ?? "Admin";
        ToolRegistration.RegisterAll(_registry, _serviceProvider);
        await _server.RunAsync(stoppingToken);
    }
}
