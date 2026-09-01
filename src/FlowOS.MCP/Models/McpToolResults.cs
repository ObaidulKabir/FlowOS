using FlowOS.Domain.Validation;
using Newtonsoft.Json;

namespace FlowOS.MCP.Models;

public static class McpToolResults
{
    public static CallToolResult Success(object data) => Json(new
    {
        ok = true,
        data
    });

    public static CallToolResult Fail(string code, string message, object? context = null) => Json(new
    {
        ok = false,
        errorCode = code,
        message,
        context
    }, true);

    public static CallToolResult ValidationFailed(ValidationResult validation) => Json(new
    {
        ok = false,
        errorCode = "MCP-VALIDATION",
        message = "WorkflowClass validation failed.",
        errors = validation.Errors.Select(error => new
        {
            code = error.Code,
            category = error.Category,
            message = error.Message,
            element = error.Element
        })
    }, true);

    private static CallToolResult Json(object value, bool isError = false) => new()
    {
        IsError = isError,
        Content =
        [
            new ToolContent
            {
                Type = "text",
                Text = JsonConvert.SerializeObject(value, Formatting.None)
            }
        ]
    };
}
