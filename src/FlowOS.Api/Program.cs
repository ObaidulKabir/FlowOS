using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MediatR;
using FlowOS.Core.Interfaces;
using FlowOS.Application.Common.Interfaces;
using FlowOS.API.Services;
using FlowOS.Security.Policies;
using FlowOS.Application.Behaviors;
using FlowOS.API.Filters;
using FlowOS.Security.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Infrastructure.Services;
using FlowOS.Infrastructure;
using FlowOS.Domain.Validation;
using FlowOS.Api.Middleware;
using FlowOS.Notifications.Application;
using FlowOS.Notifications.Infrastructure.Persistence;
using FlowOS.Workflows.Engine;
using FlowOS.StateMachines.Engine;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
    options.Filters.Add<ApiExceptionFilterAttribute>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<FlowOSDbContext>("Database");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();

builder.Services.AddAuthentication("Mock")
    .AddScheme<AuthenticationSchemeOptions, MockAuthenticationHandler>("Mock", null);

builder.Services.AddScoped<IPolicyProvider, EfCorePolicyProvider>();
builder.Services.AddScoped<IPolicyEvaluator, DefaultPolicyEvaluator>();

builder.Services.AddSingleton<NotificationStreamService>();
builder.Services.AddScoped<EventPublishingInterceptor>();
builder.Services.AddScoped<NotificationRepository>();
builder.Services.AddScoped<INotificationRepository>(sp => sp.GetRequiredService<NotificationRepository>());
builder.Services.AddScoped<INotificationQueryService>(sp => sp.GetRequiredService<NotificationRepository>());

builder.Services.AddDbContext<FlowOSDbContext>((sp, options) =>
{
    var interceptor = sp.GetRequiredService<EventPublishingInterceptor>();
    FlowOsDatabase.Configure(
        options,
        builder.Environment.EnvironmentName,
        builder.Configuration,
        "FlowOS_Db",
        interceptor);
});

builder.Services.AddFlowOSPersistence();
builder.Services.AddScoped<IEventRegistry, EventRegistry>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICapabilityService, CapabilityService>();

// Domain engines (stateless) — register once for DI/testability
builder.Services.AddSingleton<WorkflowEngine>();
builder.Services.AddSingleton<StateMachineEngine>();

builder.Services.AddScoped<FlowOS.Domain.Services.WorkflowClassValidator>();
builder.Services.AddScoped<FlowOS.Domain.Services.IWorkflowClassValidator, FlowOS.Domain.Services.WorkflowClassValidator>();
builder.Services.AddScoped<IWorkflowJsonLinter, WorkflowJsonLinter>();
builder.Services.AddScoped<FlowOS.Domain.Services.IWorkflowClassManager, FlowOS.Domain.Services.WorkflowClassManager>();
builder.Services.AddScoped<FlowOS.Domain.Services.IWorkflowClassVersionManager, FlowOS.Domain.Services.WorkflowClassVersionManager>();
builder.Services.AddScoped<FlowOS.Application.Common.Interfaces.IWorkflowCopilotService, FlowOS.Application.Services.WorkflowCopilotService>();
builder.Services.AddScoped<IWorkflowTimeTravelService, WorkflowTimeTravelService>();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(
        typeof(FlowOS.Application.Commands.StartWorkflowCommand).Assembly,
        typeof(FlowOS.Notifications.Application.NotificationProjector).Assembly
    );
    cfg.AddOpenBehavior(typeof(PolicyEnforcementBehavior<,>));
});

builder.Services.AddHostedService<FlowOS.Infrastructure.BackgroundServices.OutboxProcessorService>();
builder.Services.AddHostedService<FlowOS.Infrastructure.BackgroundServices.WorkflowTimerProcessorService>();
builder.Services.AddHostedService<FlowOS.Infrastructure.BackgroundServices.InMemoryDataCleanupService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

var app = builder.Build();

app.UseCors("AllowAll");

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<MockAuthMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => true
});

app.MapGet("/.well-known/mcp", (HttpContext context) =>
{
    var mcpUrl = FlowOsPublicUrls.McpEndpoint(FlowOsPublicUrls.ResolveOrigin(
        context.RequestServices.GetRequiredService<IConfiguration>(),
        context.Request.Scheme,
        context.Request.Host.Value,
        context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault(),
        context.Request.Headers["X-Forwarded-Host"].FirstOrDefault()));
    var mcpPath = FlowOsPublicUrls.McpPath(mcpUrl);

    var accepts = context.Request.Headers.Accept.ToString();
    if (accepts.Contains("text/html", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Redirect(mcpPath);
    }

    return Results.Ok(new
    {
        schema = "https://modelcontextprotocol.io/schema/discovery.json",
        name = "FlowOS MCP Control Plane",
        version = "1.0.0",
        description = "Multi-tenant, state-machine-governed workflow control plane with an MCP interface for safe AI-agent interaction. " + FlowOsPublicUrls.AgentJsonRpcRule,
        transport = "streamable-http",
        protocolVersion = "2025-03-26",
        endpoint = mcpPath,
        url = mcpUrl,
        documentation = mcpUrl,
        toolsEndpoint = mcpPath,
        connection = new
        {
            jsonrpcUrl = mcpUrl,
            jsonrpcPath = mcpPath,
            rule = FlowOsPublicUrls.AgentJsonRpcRule,
            accept = "application/json, text/event-stream",
            apiKeyHeader = "X-MCP-API-Key",
            tenantHeader = "x-tenant-id",
            followRedirectsOnPost = false
        },
        auth = new
        {
            type = "apiKey",
            header = "X-MCP-API-Key",
            tenantHeader = "x-tenant-id",
            sandboxAllowed = true
        },
        endpoints = new
        {
            discovery = mcpPath,
            jsonrpc = mcpPath,
            wellKnown = "/.well-known/mcp",
            sse = mcpPath,
            health = "/health/ready"
        }
    });
});

app.MapGet("/.well-known/mcp.json", () => Results.Redirect("/.well-known/mcp"));
app.MapGet("/sse", (HttpContext context) =>
{
    var mcpUrl = FlowOsPublicUrls.McpEndpoint(FlowOsPublicUrls.ResolveOrigin(
        context.RequestServices.GetRequiredService<IConfiguration>(),
        context.Request.Scheme,
        context.Request.Host.Value,
        context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault(),
        context.Request.Headers["X-Forwarded-Host"].FirstOrDefault()));
    return Results.Redirect(FlowOsPublicUrls.McpPath(mcpUrl));
});

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    int maxRetries = 30;
    int delaySeconds = 2;
    for (int retry = 1; retry <= maxRetries; retry++)
    {
        try
        {
            if (context.Database.IsRelational())
            {
                logger.LogInformation("Attempting database migration (attempt {Retry}/{Max})...", retry, maxRetries);
                context.Database.Migrate();
            }
            else
            {
                context.Database.EnsureCreated();
            }
            break;
        }
        catch (Exception ex)
        {
            if (retry < maxRetries)
            {
                logger.LogWarning(ex, "Database connection/migration failed on attempt {Retry}. Retrying in {Delay}s...", retry, delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                continue;
            }

            logger.LogError(ex, "Database migration failed after {Max} attempts; the API will still listen.", maxRetries);
        }
    }

    try
    {
        await DataSeeder.SeedAsync(context, scope.ServiceProvider, app.Environment);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Data seeding failed; the API will still listen so /api is not a 502.");
    }
}

app.Run();

public partial class Program { }
