using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MediatR;
using System.Reflection;
using FlowOS.Application.Common.Interfaces;
using FlowOS.API.Services;
using FlowOS.Security.Policies;
using FlowOS.Application.Behaviors;

using FlowOS.API.Filters;

using FlowOS.Infrastructure.Services;
using FlowOS.Core.Interfaces;

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

// Add MediatR
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssemblies(
        typeof(FlowOS.Application.Commands.StartWorkflowCommand).Assembly,
        typeof(FlowOS.Application.Handlers.WorkflowCommandHandlers).Assembly
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
}

app.MapControllers();

// Seed Data
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
    // Ensure DB is created (in-memory)
    context.Database.EnsureCreated();
    
    await DataSeeder.SeedAsync(context, scope.ServiceProvider, app.Environment);
}

app.Run();

// Make Program public for integration tests
public partial class Program { }
