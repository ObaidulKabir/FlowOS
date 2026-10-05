// DTOs for AI‑generated Business Context
namespace FlowOS.Application.DTOs;

/// <summary>
/// Request sent to the AI generation endpoint.
/// The tenant can optionally specify a TenantId – if omitted the header is used.
/// </summary>
public record GenerateBusinessContextRequest(
    Guid? TenantId,
    string BusinessCaseDescription,
    string DesiredName);

/// <summary>
/// Result produced by the AI service – a declarative JSON schema and an example payload.
/// </summary>
public record GeneratedBusinessContextDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string SchemaJson,
    string ExamplePayloadJson);
