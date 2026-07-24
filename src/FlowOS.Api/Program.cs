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
    bool useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");

    if (useInMemory)
    {
        options.UseInMemoryDatabase("FlowOS_Db")
               .AddInterceptors(interceptor);
    }
    else
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrEmpty(connectionString))
        {
            options.UseNpgsql(connectionString)
                   .AddInterceptors(interceptor);
        }
        else
        {
            throw new InvalidOperationException("Database connection string 'DefaultConnection' is missing.");
        }
    }
});

builder.Services.AddFlowOSPersistence();
builder.Services.AddScoped<IEventRegistry, EventRegistry>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICapabilityService, CapabilityService>();

// Domain engines (stateless) — register once for DI/testability
builder.Services.AddSingleton<WorkflowEngine>();
builder.Services.AddSingleton<StateMachineEngine>();

builder.Services.AddScoped<FlowOS.Domain.Services.WorkflowClassValidator>();
builder.Services.AddScoped<IWorkflowJsonLinter, WorkflowJsonLinter>();
builder.Services.AddScoped<FlowOS.Domain.Services.IWorkflowClassManager, FlowOS.Domain.Services.WorkflowClassManager>();
builder.Services.AddScoped<FlowOS.Domain.Services.IWorkflowClassVersionManager, FlowOS.Domain.Services.WorkflowClassVersionManager>();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(
        typeof(FlowOS.Application.Commands.StartWorkflowCommand).Assembly,
        typeof(FlowOS.Notifications.Application.NotificationProjector).Assembly
    );
    cfg.AddOpenBehavior(typeof(PolicyEnforcementBehavior<,>));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowDashboard",
        policy =>
        {
            policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("AllowDashboard");
}

app.UseMiddleware<MockAuthMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

    if (context.Database.IsRelational())
    {
        context.Database.Migrate();
    }
    else
    {
        context.Database.EnsureCreated();
    }

    await DataSeeder.SeedAsync(context, scope.ServiceProvider, app.Environment);
}

app.Run();

public partial class Program { }
