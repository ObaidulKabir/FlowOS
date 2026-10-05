using System.Text.Json;
using FlowOS.Application.DTOs;
using FlowOS.Core.Interfaces;

namespace FlowOS.Application.Services;

/// <summary>
/// Generates a declarative business‑context (JSON schema + example payload) using an AI agent.
/// In this prototype we mock the AI call – in production you could plug‑in OpenAI, Azure AOAI, etc.
/// </summary>
public class AiBusinessContextGenerator
{
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AiBusinessContextGenerator> _logger;

    public AiBusinessContextGenerator(ICurrentUser currentUser, ILogger<AiBusinessContextGenerator> logger) =>
        (_currentUser, _logger) = (currentUser, logger);

    /// <summary>
    /// Generates a schema and example payload based on a free‑form business‑case description.
    /// </summary>
    public async Task<GeneratedBusinessContextDto> GenerateAsync(
        Guid tenantId,
        string businessCaseDescription,
        string desiredName)
    {
        // Simulate latency of an external AI service
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // Very naive mock – in a real implementation you would call an LLM with a prompt
        var schema = new
        {
            type = "object",
            properties = new
            {
                description = new { type = "string" },
                amount = new { type = "number" },
                timestamp = new { type = "string", format = "date-time" }
            },
            required = new[] { "description", "amount" }
        };
        var example = new
        {
            description = businessCaseDescription,
            amount = 1234.56,
            timestamp = DateTime.UtcNow.ToString("o")
        };

        var result = new GeneratedBusinessContextDto(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            Name: desiredName,
            SchemaJson: JsonSerializer.Serialize(schema),
            ExamplePayloadJson: JsonSerializer.Serialize(example)
        );

        _logger.LogInformation("Generated AI business context {Name} for tenant {TenantId}", desiredName, tenantId);
        return result;
    }
}
