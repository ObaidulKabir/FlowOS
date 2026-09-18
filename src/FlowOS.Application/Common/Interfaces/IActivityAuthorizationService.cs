using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Common.Interfaces;

/// <summary>
/// Capability execution gate for human/agent workflow activities.
/// Roles are inbox labels only; this service never authorizes by role name.
/// </summary>
public interface IActivityAuthorizationService
{
    Task AuthorizeAsync(
        Guid tenantId,
        IEnumerable<string>? callerRoles,
        IReadOnlyCollection<string> requiredCapabilities,
        bool failClosed,
        string policyName,
        string activityDescription,
        CancellationToken cancellationToken = default);

    Task<bool> IsAuthorizedAsync(
        Guid tenantId,
        IEnumerable<string>? callerRoles,
        IReadOnlyCollection<string> requiredCapabilities,
        bool failClosed,
        CancellationToken cancellationToken = default);
}

public static class ActivityAuthorization
{
    public static bool IsAdmin(IEnumerable<string>? roles)
        => roles != null && roles.Contains("Admin", StringComparer.OrdinalIgnoreCase);

    public static bool HasGrant(
        IReadOnlyCollection<string> callerCapabilities,
        IReadOnlyCollection<string> requiredCapabilities)
    {
        if (requiredCapabilities == null || requiredCapabilities.Count == 0)
            return false;

        var caller = callerCapabilities as HashSet<string>
            ?? new HashSet<string>(callerCapabilities ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var required in requiredCapabilities)
        {
            if (string.IsNullOrWhiteSpace(required))
                continue;
            if (caller.Contains(required))
                return true;
        }

        if (caller.Contains("event.publish") &&
            requiredCapabilities.Any(cap =>
                !string.IsNullOrWhiteSpace(cap) &&
                cap.StartsWith("event.publish.", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    public static List<string> NormalizeCapabilities(IEnumerable<string>? capabilities)
        => (capabilities ?? Enumerable.Empty<string>())
            .Where(cap => !string.IsNullOrWhiteSpace(cap))
            .Select(cap => cap.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static List<string> ResolveRequiredCapabilities(
        WorkflowStepDefinition? step,
        string? eventType = null)
    {
        if (step == null)
            return new List<string>();

        if (!string.IsNullOrWhiteSpace(eventType) &&
            step.EventRequiredCapabilities != null &&
            TryGetValue(step.EventRequiredCapabilities, eventType, out var eventCaps) &&
            eventCaps.Count > 0)
        {
            return NormalizeCapabilities(eventCaps);
        }

        return NormalizeCapabilities(step.RequiredCapabilities);
    }

    public static bool IsHumanActivity(WorkflowStepDefinition? step, string? eventType = null)
    {
        if (step == null)
            return !string.IsNullOrWhiteSpace(eventType) &&
                   eventType.StartsWith("EVT-", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(eventType) &&
            step.EventRequiredCapabilities != null &&
            TryGetValue(step.EventRequiredCapabilities, eventType, out var eventCaps) &&
            eventCaps.Count > 0)
        {
            return true;
        }

        if (step.StepType == FlowOS.Workflows.Enums.WorkflowStepType.HumanTask &&
            !string.Equals(eventType, "Default", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(eventType, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(eventType) &&
               eventType.StartsWith("EVT-", StringComparison.OrdinalIgnoreCase);
    }

    public static string MapCapability(
        string value,
        IReadOnlyDictionary<string, string>? capabilityOverrides,
        IReadOnlyDictionary<string, string>? eventAliases = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (capabilityOverrides != null &&
            TryGetMappedValue(capabilityOverrides, value, out var mapped) &&
            !string.Equals(mapped, value, StringComparison.OrdinalIgnoreCase))
        {
            return mapped;
        }

        const string eventPrefix = "event.publish.";
        if (eventAliases != null &&
            value.StartsWith(eventPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var eventId = value[eventPrefix.Length..];
            return eventPrefix + (TryGetMappedValue(eventAliases, eventId, out var aliased) ? aliased : eventId);
        }

        return value;
    }

    private static bool TryGetValue(
        IReadOnlyDictionary<string, List<string>> values,
        string key,
        out List<string> value)
    {
        foreach (var item in values)
        {
            if (!string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;
            value = item.Value ?? new List<string>();
            return true;
        }

        value = new List<string>();
        return false;
    }

    private static bool TryGetMappedValue(
        IReadOnlyDictionary<string, string> values,
        string key,
        out string value)
    {
        foreach (var item in values)
        {
            if (!string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;
            value = string.IsNullOrWhiteSpace(item.Value) ? key : item.Value.Trim();
            return true;
        }

        value = key;
        return false;
    }
}
