using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Services;

/// <summary>
/// Stamps example/simulation packs with the refined model:
/// capability is the execution gate; role is a grant bag plus inbox label.
/// </summary>
public static class WorkflowSimulationGovernance
{
    public static void Apply(WorkflowClassBlueprint blueprint)
    {
        if (blueprint?.Workflow?.Steps == null || blueprint.Events == null)
            return;

        var humanEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in blueprint.Workflow.Steps)
        {
            if (!IsHumanTask(step))
                continue;
            foreach (var eventId in HumanExitEvents(step))
                humanEventIds.Add(eventId);
        }

        foreach (var evt in blueprint.Events)
        {
            if (evt.Category == EventCategory.Human ||
                (evt.AllowedRoles ?? new List<string>()).Any(role => !IsSystemRole(role)))
            {
                humanEventIds.Add(evt.EventId);
            }
        }

        var catalog = new HashSet<string>(
            (blueprint.Capabilities ?? new List<CapabilityBlueprint>())
                .Select(item => item.Code)
                .Where(code => !string.IsNullOrWhiteSpace(code)),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < blueprint.Events.Count; i++)
        {
            var evt = blueprint.Events[i];
            var caps = Normalize(evt.RequiredCapabilities);
            var isHuman = humanEventIds.Contains(evt.EventId);
            if (isHuman && caps.Count == 0 && !string.IsNullOrWhiteSpace(evt.EventId))
                caps.Add($"event.publish.{evt.EventId.Trim()}");
            foreach (var cap in caps)
                catalog.Add(cap);

            blueprint.Events[i] = evt with
            {
                Category = isHuman ? EventCategory.Human : evt.Category,
                RequiredCapabilities = caps
            };
        }

        for (var i = 0; i < blueprint.Workflow.Steps.Count; i++)
        {
            var step = blueprint.Workflow.Steps[i];
            if (!IsHumanTask(step))
                continue;

            var caps = Normalize(step.RequiredCapabilities);
            if (caps.Count == 0)
            {
                foreach (var eventId in HumanExitEvents(step))
                    caps.Add($"event.publish.{eventId}");
            }

            foreach (var cap in caps)
                catalog.Add(cap);

            blueprint.Workflow.Steps[i] = step with { RequiredCapabilities = caps };
        }

        var grants = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in blueprint.Roles ?? new List<RoleBlueprint>())
        {
            if (string.IsNullOrWhiteSpace(existing.Name))
                continue;
            grants[existing.Name] = new HashSet<string>(
                Normalize(existing.GrantedCapabilities),
                StringComparer.OrdinalIgnoreCase);
        }

        foreach (var step in blueprint.Workflow.Steps)
        {
            if (!IsHumanTask(step))
                continue;
            foreach (var role in InboxRoles(step))
            {
                if (!grants.TryGetValue(role, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    grants[role] = set;
                }

                foreach (var cap in Normalize(step.RequiredCapabilities))
                    set.Add(cap);
            }
        }

        foreach (var evt in blueprint.Events)
        {
            foreach (var role in (evt.AllowedRoles ?? new List<string>()).Where(r => !IsSystemRole(r)))
            {
                if (!grants.TryGetValue(role, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    grants[role] = set;
                }

                foreach (var cap in Normalize(evt.RequiredCapabilities))
                    set.Add(cap);
            }
        }

        // Director may execute Manager-gated events without being the inbox role.
        if (grants.TryGetValue("Manager", out var managerCaps))
        {
            var directorInboxCaps = managerCaps
                .Where(cap => cap.StartsWith("event.publish.", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (directorInboxCaps.Count > 0)
            {
                if (!grants.TryGetValue("Director", out var directorCaps))
                {
                    directorCaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    grants["Director"] = directorCaps;
                }

                foreach (var cap in directorInboxCaps)
                    directorCaps.Add(cap);
            }
        }

        blueprint.Roles.Clear();
        foreach (var pair in grants.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            blueprint.Roles.Add(new RoleBlueprint
            {
                Name = pair.Key,
                Description = $"{pair.Key} inbox and capability grants",
                GrantedCapabilities = pair.Value.OrderBy(cap => cap, StringComparer.OrdinalIgnoreCase).ToList()
            });
        }

        var existingCaps = blueprint.Capabilities
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .ToDictionary(item => item.Code, item => item, StringComparer.OrdinalIgnoreCase);
        blueprint.Capabilities.Clear();
        foreach (var code in catalog.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            if (existingCaps.TryGetValue(code, out var existing))
            {
                blueprint.Capabilities.Add(existing);
                continue;
            }

            blueprint.Capabilities.Add(new CapabilityBlueprint
            {
                Code = code,
                Description = $"Publish or complete activity gated by {code}"
            });
        }
    }

    public static void Grant(WorkflowClassBlueprint blueprint, string roleName, params string[] capabilities)
    {
        if (blueprint == null || string.IsNullOrWhiteSpace(roleName))
            return;

        var caps = Normalize(capabilities);
        if (caps.Count == 0)
            return;

        var role = blueprint.Roles.FirstOrDefault(item =>
            string.Equals(item.Name, roleName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (role == null)
        {
            role = new RoleBlueprint
            {
                Name = roleName.Trim(),
                Description = $"{roleName.Trim()} inbox and capability grants",
                GrantedCapabilities = new List<string>()
            };
            blueprint.Roles.Add(role);
        }

        foreach (var cap in caps)
        {
            if (!role.GrantedCapabilities.Any(existing =>
                    string.Equals(existing, cap, StringComparison.OrdinalIgnoreCase)))
            {
                role.GrantedCapabilities.Add(cap);
            }

            if (!blueprint.Capabilities.Any(item =>
                    string.Equals(item.Code, cap, StringComparison.OrdinalIgnoreCase)))
            {
                blueprint.Capabilities.Add(new CapabilityBlueprint
                {
                    Code = cap,
                    Description = $"Publish or complete activity gated by {cap}"
                });
            }
        }
    }

    private static bool IsHumanTask(StepBlueprint step)
        => string.Equals(step.StepType, "HumanTask", StringComparison.OrdinalIgnoreCase);

    private static bool IsSystemRole(string? role)
        => string.IsNullOrWhiteSpace(role) ||
           string.Equals(role, "System", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(role, "Anyone", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(role, "Unassigned", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> HumanExitEvents(StepBlueprint step)
    {
        foreach (var key in (step.NextSteps ?? new Dictionary<string, string>()).Keys)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                string.Equals(key, "Default", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "SubWorkflowCompleted", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return key.Trim();
        }
    }

    private static IEnumerable<string> InboxRoles(StepBlueprint step)
        => (step.RequiredRoles ?? new List<string>())
            .Concat(step.AllowedRoles ?? Enumerable.Empty<string>())
            .Where(role => !IsSystemRole(role))
            .Select(role => role.Trim());

    private static List<string> Normalize(IEnumerable<string>? values)
        => (values ?? Enumerable.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
