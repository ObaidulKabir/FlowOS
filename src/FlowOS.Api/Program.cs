using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MediatR;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Add DbContext
builder.Services.AddDbContext<FlowOSDbContext>(options =>
{
    options.UseInMemoryDatabase("FlowOS_Db");
    options.EnableSensitiveDataLogging();
});

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(
    typeof(FlowOS.Application.Commands.StartWorkflowCommand).Assembly,
    typeof(FlowOS.Application.Handlers.WorkflowCommandHandlers).Assembly
));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapControllers();

app.Run();

// Make Program public for integration tests
public partial class Program { }
