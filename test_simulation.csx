using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FlowOS.Infrastructure.Persistence;

var services = new ServiceCollection();
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql("Host=localhost;Database=flowos;Username=postgres;Password=postgres"));

var provider = services.BuildServiceProvider();
var db = provider.GetRequiredService<ApplicationDbContext>();

var tenantId = Guid.Parse("3c1dec2a-12f0-4b8f-b0a8-12c34304581e");

var bindings = db.WorkflowContextBindings
    .Where(b => b.TenantId == tenantId)
    .ToList();

Console.WriteLine($"Found {bindings.Count} bindings for tenant");

foreach (var binding in bindings)
{
    Console.WriteLine($"Binding: {binding.ContextType} (ActiveRev: {binding.ActiveRevisionId})");
    if (binding.ActiveRevisionId.HasValue)
    {
        var rev = db.WorkflowContextBindingRevisions.FirstOrDefault(r => r.Id == binding.ActiveRevisionId.Value);
        if (rev != null)
        {
            var wf = db.WorkflowDefinitions.FirstOrDefault(w => w.Id == rev.WorkflowDefinitionId);
            var sm = db.StateMachines.FirstOrDefault(s => s.Id == rev.StateMachineDefinitionId);
            Console.WriteLine($"  WF: {wf?.Name}, SM: {sm?.InitialState}");
            if (sm != null)
            {
                Console.WriteLine("  SM States: " + string.Join(", ", sm.States));
                Console.WriteLine("  SM Transitions:");
                foreach(var t in sm.Transitions) {
                    Console.WriteLine($"    {t.FromState} -> {t.ToState} on {t.EventId}");
                }
            }
            if (wf != null)
            {
                Console.WriteLine("  WF Steps:");
                foreach(var s in wf.Steps) {
                    Console.WriteLine($"    {s.StepId} ({s.StepType})");
                    foreach(var ns in s.NextSteps) {
                        Console.WriteLine($"      -> {ns.Value} on {ns.Key}");
                    }
                }
            }
        }
    }
}
