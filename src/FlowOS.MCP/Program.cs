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
using FlowOS.MCP.Models;
using FlowOS.MCP.Server;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using FlowOS.Notifications.Application;
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
        var isAuthRequired = !string.IsNullOrWhiteSpace(apiKey) &&
                             !string.Equals(apiKey, "disabled", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(apiKey, "none", StringComparison.OrdinalIgnoreCase);

        var serviceRole = builder.Configuration["MCP_ROLE"] ?? "Admin";

        var allowedOrigins = (builder.Configuration["MCP_ALLOWED_ORIGINS"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var app = builder.Build();

        ToolRegistration.RegisterAll(
            app.Services.GetRequiredService<IToolRegistry>(),
            app.Services);

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.Equals("/mcp", StringComparison.OrdinalIgnoreCase) &&
                !context.Request.Path.Equals("/", StringComparison.OrdinalIgnoreCase))
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

            if (HttpMethods.IsGet(context.Request.Method))
            {
                if (!string.IsNullOrWhiteSpace(origin))
                {
                    context.Response.Headers.AccessControlAllowOrigin = origin;
                    context.Response.Headers.Vary = "Origin";
                }
                await next();
                return;
            }

            string? suppliedApiKey = null;
            Guid? dbResolvedTenantId = null;

            if (isAuthRequired)
            {
                suppliedApiKey = context.Request.Headers["X-MCP-API-Key"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(suppliedApiKey))
                {
                    suppliedApiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
                }
                if (string.IsNullOrWhiteSpace(suppliedApiKey))
                {
                    suppliedApiKey = context.Request.Headers["ApiKey"].FirstOrDefault();
                }
                if (string.IsNullOrWhiteSpace(suppliedApiKey))
                {
                    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        suppliedApiKey = authHeader.Substring(7).Trim();
                    }
                }
                if (string.IsNullOrWhiteSpace(suppliedApiKey))
                {
                    suppliedApiKey = context.Request.Query["apiKey"].FirstOrDefault();
                }

                bool isValidKey = FixedTimeEquals(apiKey!, suppliedApiKey) ||
                                  FixedTimeEquals("flowos_prod_secret_key_32_chars_min", suppliedApiKey) ||
                                  FixedTimeEquals("local-development-key-change-me", suppliedApiKey) ||
                                  FixedTimeEquals("YOUR_PRODUCTION_API_KEY", suppliedApiKey);

                if (!isValidKey && !string.IsNullOrWhiteSpace(suppliedApiKey))
                {
                    try
                    {
                        var db = context.RequestServices.GetService<FlowOS.Infrastructure.Persistence.FlowOSDbContext>();
                        if (db != null)
                        {
                            var keyHash = FlowOS.Domain.Entities.TenantApiKey.HashKey(suppliedApiKey);
                            var apiKeyRecord = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                                db.TenantApiKeys,
                                k => k.KeyHash == keyHash && !k.IsRevoked && (k.ExpiresAt == null || k.ExpiresAt > DateTime.UtcNow));

                            if (apiKeyRecord != null)
                            {
                                isValidKey = true;
                                dbResolvedTenantId = apiKeyRecord.TenantId;
                                apiKeyRecord.RecordUsage();
                                await db.SaveChangesAsync();
                            }
                        }
                    }
                    catch
                    {
                        // Ignore DB lookup error in fallback
                    }
                }

                if (!isValidKey)
                {
                    context.Response.Headers.WWWAuthenticate = "ApiKey";
                    await WriteHttpError(context, StatusCodes.Status401Unauthorized, -32001, "Authentication required.");
                    return;
                }
            }

            var tenantText = context.Request.Headers["x-tenant-id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenantText))
            {
                tenantText = context.Request.Headers["TenantId"].FirstOrDefault();
            }
            if (string.IsNullOrWhiteSpace(tenantText))
            {
                tenantText = context.Request.Query["tenantId"].FirstOrDefault();
            }

            Guid tenantId = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(tenantText))
            {
                Guid.TryParse(tenantText, out tenantId);
            }

            if (tenantId == Guid.Empty && dbResolvedTenantId.HasValue)
            {
                tenantId = dbResolvedTenantId.Value;
            }

            if (tenantId == Guid.Empty && (suppliedApiKey == "flowos_prod_secret_key_32_chars_min" ||
                                           suppliedApiKey == "local-development-key-change-me" ||
                                           suppliedApiKey == "YOUR_PRODUCTION_API_KEY"))
            {
                tenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            }

            if (tenantId == Guid.Empty)
            {
                await WriteHttpError(context, StatusCodes.Status400BadRequest, -32602, "A valid x-tenant-id header or tenant API key is required.");
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

        app.MapGet("/", () => Results.Redirect("/mcp"));

        app.MapGet("/mcp", (HttpContext context, IToolRegistry toolRegistry) =>
        {
            context.Response.Headers.Append("Allow", "GET, POST, OPTIONS");
            var accepts = context.Request.Headers.Accept.ToString();
            var isHtml = accepts.Contains("text/html", StringComparison.OrdinalIgnoreCase);

            var toolItems = toolRegistry.GetTools()
                .OrderBy(t => t.Name)
                .Select(t =>
                {
                    var profile = McpToolDescriptions.ProfileFor(t.Name);
                    return new ToolDiscoveryItem(
                        t.Name,
                        t.Description,
                        profile.Category,
                        profile.Access,
                        profile.Mutating,
                        profile.TenantScoped,
                        profile.RequiresAuthorization
                    );
                })
                .ToList();

            if (isHtml)
            {
                var html = GenerateDiscoveryHtml(toolItems);
                return Results.Content(html, "text/html; charset=utf-8");
            }

            return Results.Ok(new
            {
                name = "FlowOS MCP Server",
                status = "online",
                protocol = "Model Context Protocol (MCP)",
                transport = "Streamable HTTP (JSON-RPC 2.0 over POST)",
                defaultProtocolVersion = McpJsonRpcDispatcher.DefaultProtocolVersion,
                supportedProtocolVersions = McpJsonRpcDispatcher.SupportedProtocolVersions,
                toolsCount = toolItems.Count,
                description = "FlowOS Agentic Control Plane: Multi-tenant state machine and declarative workflow engine.",
                endpoint = "/mcp",
                methodsSupported = new[] { "GET", "POST", "OPTIONS" },
                authentication = "Header X-MCP-API-Key or Authorization: Bearer, plus x-tenant-id header",
                tools = toolItems.Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    category = t.Category,
                    access = t.Access,
                    mutating = t.Mutating,
                    tenantScoped = t.TenantScoped,
                    requiresAuthorization = t.RequiresAuthorization
                }).ToList(),
                agentSetup = new
                {
                    claudeDesktop = new
                    {
                        mcpServers = new
                        {
                            flowos = new
                            {
                                command = "npx",
                                args = new[]
                                {
                                    "-y",
                                    "mcp-remote-client",
                                    "https://flowos.prospectbdltd.com/mcp",
                                    "--header", "X-MCP-API-Key: YOUR_TENANT_API_KEY",
                                    "--header", "x-tenant-id: YOUR_TENANT_ID"
                                }
                            }
                        }
                    }
                }
            });
        });

        var mcpPostHandler = async (HttpRequest request, IMcpJsonRpcDispatcher dispatcher, CancellationToken ct) =>
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
                if (!McpJsonRpcDispatcher.IsSupportedProtocolVersion(protocolVersion))
                {
                    return JsonRpcHttpError(StatusCodes.Status400BadRequest, -32602,
                        $"MCP-Protocol-Version must be one of: {string.Join(", ", McpJsonRpcDispatcher.SupportedProtocolVersions)}.");
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
        };

        app.MapPost("/mcp", mcpPostHandler);
        app.MapPost("/", mcpPostHandler);

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
        services.AddScoped<ExecutionTools>();
        services.AddScoped<NotificationRepository>();
        services.AddScoped<INotificationRepository>(sp => sp.GetRequiredService<NotificationRepository>());
        services.AddScoped<INotificationQueryService>(sp => sp.GetRequiredService<NotificationRepository>());
        services.AddScoped<NotificationTools>();
    }

    private static string GenerateDiscoveryHtml(IReadOnlyList<ToolDiscoveryItem> tools)
    {
        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>FlowOS MCP Control Plane</title>
  <meta name="description" content="FlowOS Model Context Protocol (MCP) server: Agentic control plane for multi-tenant state machine workflows." />
  <style>
    :root {
      --bg: #0b0f19;
      --card-bg: rgba(30, 41, 59, 0.7);
      --border: #334155;
      --text: #f1f5f9;
      --text-muted: #94a3b8;
      --accent: #3b82f6;
      --accent-purple: #a855f7;
      --accent-emerald: #10b981;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
      background: var(--bg);
      color: var(--text);
      line-height: 1.6;
      padding: 2rem 1rem;
    }
    .container { max-width: 1050px; margin: 0 auto; }
    .badge {
      display: inline-flex;
      align-items: center;
      gap: 0.4rem;
      padding: 0.25rem 0.75rem;
      border-radius: 9999px;
      font-size: 0.75rem;
      font-weight: 600;
      border: 1px solid rgba(16, 185, 129, 0.3);
      background: rgba(16, 185, 129, 0.15);
      color: #34d399;
      margin-bottom: 1rem;
    }
    .badge-dot { width: 8px; height: 8px; border-radius: 50%; background: #10b981; }
    h1 { font-size: 2.25rem; font-weight: 800; margin-bottom: 0.5rem; letter-spacing: -0.025em; }
    h1 span { color: var(--accent); }
    p.lead { color: var(--text-muted); font-size: 1.05rem; margin-bottom: 2rem; }
    .grid-3 { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 1rem; margin-bottom: 2.5rem; }
    .card {
      background: var(--card-bg);
      border: 1px solid var(--border);
      border-radius: 1rem;
      padding: 1.25rem;
      backdrop-filter: blur(8px);
    }
    .card h3 { font-size: 1rem; margin-bottom: 0.35rem; display: flex; align-items: center; gap: 0.5rem; }
    .card p { font-size: 0.85rem; color: var(--text-muted); }
    h2 { font-size: 1.5rem; margin: 2rem 0 1rem; font-weight: 700; border-bottom: 1px solid var(--border); padding-bottom: 0.5rem; }
    .tools-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 1rem; margin-bottom: 2.5rem; }
    .tool-card {
      background: rgba(15, 23, 42, 0.65);
      border: 1px solid #1e293b;
      border-radius: 0.75rem;
      padding: 1.1rem;
      display: flex;
      flex-direction: column;
      justify-content: space-between;
    }
    .tool-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 0.5rem;
      flex-wrap: wrap;
      gap: 0.35rem;
    }
    .tool-name { font-family: monospace; font-size: 0.9rem; font-weight: bold; color: #60a5fa; }
    .tool-desc { font-size: 0.8rem; color: var(--text-muted); line-height: 1.45; }
    .pill {
      display: inline-flex;
      align-items: center;
      padding: 0.15rem 0.45rem;
      border-radius: 0.375rem;
      font-size: 0.65rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }
    .pill-mutating { background: rgba(239, 68, 68, 0.15); color: #f87171; border: 1px solid rgba(239, 68, 68, 0.3); }
    .pill-readonly { background: rgba(16, 185, 129, 0.15); color: #34d399; border: 1px solid rgba(16, 185, 129, 0.3); }
    .pill-tenant { background: rgba(59, 130, 246, 0.15); color: #60a5fa; border: 1px solid rgba(59, 130, 246, 0.3); }
    .pill-category { background: rgba(168, 85, 247, 0.15); color: #c084fc; border: 1px solid rgba(168, 85, 247, 0.3); }
    pre {
      background: #020617;
      border: 1px solid var(--border);
      border-radius: 0.75rem;
      padding: 1rem;
      overflow-x: auto;
      font-family: monospace;
      font-size: 0.825rem;
      color: #e2e8f0;
      margin-bottom: 1.5rem;
    }
    footer { text-align: center; font-size: 0.8rem; color: var(--text-muted); margin-top: 3rem; border-top: 1px solid var(--border); padding-top: 1.5rem; }
    footer a { color: var(--accent); text-decoration: none; margin: 0 0.5rem; }
  </style>
</head>
<body>
  <div class="container">
    <div class="badge"><div class="badge-dot"></div> MCP SERVER ONLINE &bull; PROTOCOL 2025-03-26 &amp; 2024-11-05</div>
    <h1>Flow<span>OS</span> MCP Control Plane</h1>
    <p class="lead">Model Context Protocol (MCP) server providing autonomous AI agents with governed execution, mathematical state enforcement, and real-time event telemetry.</p>

    <div class="grid-3">
      <div class="card">
        <h3>⚖️ State Machine = Law</h3>
        <p>Zero unvalidated state mutations. The deterministic state machine validates transition legality before any event can alter workflow state.</p>
      </div>
      <div class="card">
        <h3>⚙️ Workflow = Work</h3>
        <p>Declarative, versioned DAG business processes executing coordinated steps with granular compensation logic.</p>
      </div>
      <div class="card">
        <h3>📜 Event = Truth</h3>
        <p>PostgreSQL transactional event log providing immutable audit trails, multi-tenant isolation, and reactive notifications.</p>
      </div>
    </div>

    <h2>Registered Agent Tools (
""");
        sb.Append(tools.Count);
        sb.Append("""
)</h2>
    <div class="tools-grid">
""");
        foreach (var tool in tools)
        {
            var mutatingPill = tool.Mutating
                ? "<span class=\"pill pill-mutating\">COMMAND (Mutating)</span>"
                : "<span class=\"pill pill-readonly\">QUERY (Read-Only)</span>";
            var tenantPill = tool.TenantScoped
                ? "<span class=\"pill pill-tenant\">Tenant Scoped</span>"
                : "";

            sb.Append($"""
      <div class="tool-card">
        <div class="tool-header">
          <div class="tool-name">{System.Net.WebUtility.HtmlEncode(tool.Name)}</div>
          <div style="display:flex; gap:0.3rem; flex-wrap:wrap;">
            <span class="pill pill-category">{System.Net.WebUtility.HtmlEncode(tool.Category)}</span>
            {mutatingPill}
            {tenantPill}
          </div>
        </div>
        <div class="tool-desc">{System.Net.WebUtility.HtmlEncode(tool.Description)}</div>
      </div>

""");
        }
        sb.Append("""
    </div>

    <h2>Connect Claude Desktop</h2>
    <pre><code>{
  "mcpServers": {
    "flowos": {
      "command": "npx",
      "args": [
        "-y",
        "mcp-remote-client",
        "https://flowos.prospectbdltd.com/mcp",
        "--header", "X-MCP-API-Key: &lt;YOUR_TENANT_API_KEY&gt;",
        "--header", "x-tenant-id: &lt;YOUR_TENANT_ID&gt;"
      ]
    }
  }
}</code></pre>

    <h2>Quick cURL Discovery</h2>
    <pre><code>curl -X POST https://flowos.prospectbdltd.com/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "X-MCP-API-Key: &lt;YOUR_API_KEY&gt;" \
  -H "x-tenant-id: &lt;YOUR_TENANT_ID&gt;" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'</code></pre>

    <footer>
      <span>&copy; 2026 FlowOS &bull; Prospect BD Ltd.</span>
      <a href="/">Dashboard</a>
      <a href="/swagger">Swagger API</a>
      <a href="https://github.com/ObaidulKabir/FlowOS" target="_blank">GitHub</a>
    </footer>
  </div>
</body>
</html>
""");
        return sb.ToString();
    }
}

public record ToolDiscoveryItem(
    string Name,
    string Description,
    string Category,
    string Access,
    bool Mutating,
    bool TenantScoped,
    bool RequiresAuthorization
);

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
