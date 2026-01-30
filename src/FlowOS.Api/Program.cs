using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MediatR;
using System.Reflection;
using FlowOS.Application.Common.Interfaces;
using FlowOS.API.Services;
using FlowOS.Security.Policies;
using FlowOS.Application.Behaviors;

using FlowOS.API.Filters;

using FlowOS.Application.Common.Interfaces;
using FlowOS.Security.Interfaces; // Add this
using FlowOS.Infrastructure.Services; // Ensure this is present
using FlowOS.Core.Interfaces;

using FlowOS.Api.Middleware; // Add this

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers(options => 
    options.Filters.Add<ApiExceptionFilterAttribute>());

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add HttpContextAccessor and CurrentUser
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();

// Add Policy Services
builder.Services.AddScoped<IPolicyProvider, AllowAllPolicyProvider>();
builder.Services.AddScoped<IPolicyEvaluator, DefaultPolicyEvaluator>();

// Add DbContext
builder.Services.AddDbContext<FlowOSDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrEmpty(connectionString))
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        // Enforce persistent DB (except for Unit Tests which replace this)
        // If we are running the API (Dev/Prod) and no connection string is provided, fail fast.
        throw new InvalidOperationException("Database connection string 'DefaultConnection' is missing. You must configure a valid PostgreSQL connection.");
    }
});

// Add Event Registry
builder.Services.AddScoped<IEventRegistry, EventRegistry>();
builder.Services.AddMemoryCache(); // Add this
builder.Services.AddScoped<ICapabilityService, CapabilityService>();

// Add MediatR
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssemblies(
        typeof(FlowOS.Application.Commands.StartWorkflowCommand).Assembly,
        typeof(FlowOS.Application.Handlers.WorkflowCommandHandlers).Assembly,
        typeof(FlowOS.Application.Handlers.StateMachineQueryHandlers).Assembly // Ensure new handlers are registered
    );
    // Register Pipeline Behaviors
    cfg.AddOpenBehavior(typeof(PolicyEnforcementBehavior<,>));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    
    // Use Mock Auth for Development Testing
    app.UseMiddleware<MockAuthMiddleware>();
}

app.MapControllers();

// Seed Data
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
    // context.Database.EnsureCreated(); // Replaced with Migrate() for schema evolution
    context.Database.Migrate();
    
    await DataSeeder.SeedAsync(context, scope.ServiceProvider, app.Environment);
}

app.Run();

// Make Program public for integration tests
public partial class Program { }
