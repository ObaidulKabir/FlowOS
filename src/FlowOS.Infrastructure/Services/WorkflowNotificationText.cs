using System.Text.Json;

namespace FlowOS.Infrastructure.Services;

/// <summary>
/// In-app notification line written by the outbox. The sentence is <c>template</c>.
/// <c>target</c> is the recipient and is shown only inside <c>(Target: …)</c>.
/// </summary>
public static class WorkflowNotificationText
{
    public static string Format(JsonElement root)
    {
        var stepId = root.TryGetProperty("stepId", out var sid) ? sid.GetString() ?? "" : "";
        var capability = root.TryGetProperty("capability", out var cap) ? cap.GetString() : null;
        var target = root.TryGetProperty("url", out var url) ? url.GetString() : (root.TryGetProperty("target", out var tg) ? tg.GetString() : null);
        if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(capability))
            target = capability;

        var template = root.TryGetProperty("template", out var tm) ? tm.GetString() : "Workflow action triggered";
        return $"[{stepId}] {template} (Target: {target})";
    }
}
