using System.Net.Http.Json;

// Configuration
var baseUrl = "http://localhost:5005";
var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var workflowDefinitionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

Console.WriteLine("FlowOS Client - Hello World Test");
Console.WriteLine("--------------------------------");

using var client = new HttpClient();
client.BaseAddress = new Uri(baseUrl);

try 
{
    // 1. Start Workflow
    Console.WriteLine($"Starting Workflow (Def: {workflowDefinitionId})...");
    
    var startCommand = new 
    {
        TenantId = tenantId,
        WorkflowDefinitionId = workflowDefinitionId,
        Version = 1,
        InitialStepId = "Start"
    };

    var response = await client.PostAsJsonAsync("/api/workflows/start", startCommand);
    
    if (response.IsSuccessStatusCode)
    {
        var result = await response.Content.ReadFromJsonAsync<StartWorkflowResponse>();
        Console.WriteLine($"✅ Workflow Started! Instance ID: {result?.WorkflowInstanceId}");
        
        // 2. Check Status (Optional - if we had a Get endpoint implemented)
        // var statusResponse = await client.GetAsync($"/api/workflows/{result?.WorkflowInstanceId}");
        // ...
    }
    else
    {
        var error = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"❌ Failed to start workflow. Status: {response.StatusCode}");
        Console.WriteLine($"Error: {error}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Exception: {ex.Message}");
    Console.WriteLine("Ensure the API is running at http://localhost:5005");
}

Console.WriteLine("\nPress any key to exit...");
// Console.ReadKey(); // Commented out for automated environments

// DTOs
record StartWorkflowResponse(Guid WorkflowInstanceId);
