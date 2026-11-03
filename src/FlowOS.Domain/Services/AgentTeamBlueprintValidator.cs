using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities.Orchestration;
using FlowOS.Domain.Validation;

namespace FlowOS.Domain.Services;

public interface IAgentTeamBlueprintValidator
{
    ValidationResult Validate(AgentTeamBlueprint? blueprint);
}

/// <summary>
/// Validates an <see cref="AgentTeamBlueprint"/> before it is stored or published.
///
/// Structural only, exactly like <see cref="AgentWorkflowBlueprintValidator"/>: a slot's
/// <see cref="AgentTeamSlotBlueprint.RequiredCapabilities"/> is checked for shape, never
/// resolved against a tenant's registry or against any particular workflow's declared
/// agents. That resolution happens where the team is actually bound to a plan, which is
/// the first moment there is a concrete <see cref="AgentDefinitionBlueprint"/> to check it
/// against (ADR-007 §2, §9).
/// </summary>
public sealed class AgentTeamBlueprintValidator : IAgentTeamBlueprintValidator
{
    public ValidationResult Validate(AgentTeamBlueprint? blueprint)
    {
        var result = new ValidationResult();

        if (blueprint is null)
        {
            result.AddError("ATM-STR-000", "Structural", "Definition is required.", "Blueprint");
            return result;
        }

        if (string.IsNullOrWhiteSpace(blueprint.Name))
        {
            result.AddError("ATM-STR-001", "Structural", "Name is required.", "Blueprint");
        }

        if (string.IsNullOrWhiteSpace(blueprint.Objective))
        {
            result.AddError(
                "ATM-STR-002",
                "Structural",
                "Objective is required — it states what the team exists to do.",
                "Blueprint");
        }

        if (!string.IsNullOrWhiteSpace(blueprint.MaxAutonomyLevel) && !IsKnownAutonomyLevel(blueprint.MaxAutonomyLevel))
        {
            result.AddError(
                "ATM-STR-003",
                "Structural",
                $"Unknown maxAutonomyLevel '{blueprint.MaxAutonomyLevel}'.",
                "Blueprint");
        }

        var slots = blueprint.Slots ?? new List<AgentTeamSlotBlueprint>();

        if (slots.Count == 0)
        {
            result.AddError("ATM-STR-004", "Structural", "A team must declare at least one slot.", "Slots");
            return result;
        }

        foreach (var slot in slots)
        {
            if (string.IsNullOrWhiteSpace(slot.RoleName))
            {
                result.AddError("ATM-SLOT-001", "Structural", "A slot's roleName cannot be empty.", "Slots");
            }

            foreach (var tag in slot.RequiredCapabilities ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    result.AddError(
                        "ATM-SLOT-002",
                        "Structural",
                        $"Slot '{slot.RoleName}' declares an empty required capability.",
                        "Slots");
                }
            }

            if (!string.IsNullOrWhiteSpace(slot.DefaultDefinitionAlias) && !string.IsNullOrWhiteSpace(slot.DefaultProfileAlias))
            {
                result.AddError(
                    "ATM-SLOT-003",
                    "Structural",
                    $"Slot '{slot.RoleName}' declares both defaultDefinitionAlias and defaultProfileAlias. " +
                    "Declare one — defaultDefinitionAlias is the portable path; defaultProfileAlias is a " +
                    "deprecated, tenant-scoped escape hatch.",
                    "Slots");
            }
        }

        var duplicates = slots
            .Where(s => !string.IsNullOrWhiteSpace(s.RoleName))
            .GroupBy(s => s.RoleName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var duplicate in duplicates)
        {
            result.AddError(
                "ATM-SLOT-004",
                "Structural",
                $"Slot role '{duplicate}' is declared more than once. Role names must be unique within a team.",
                "Slots");
        }

        return result;
    }

    /// <summary>
    /// True when the value names a defined <see cref="AgentAutonomyLevel"/>.
    /// <c>Enum.TryParse</c> alone is not enough: it accepts any numeric string, so "99"
    /// would parse to an undefined member. <c>Enum.IsDefined</c> closes that.
    /// </summary>
    private static bool IsKnownAutonomyLevel(string value) =>
        Enum.TryParse<AgentAutonomyLevel>(value.Trim(), ignoreCase: true, out var level) &&
        Enum.IsDefined(level);
}
