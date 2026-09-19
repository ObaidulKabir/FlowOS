using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.ValueObjects;

namespace FlowOS.Domain.Services;

/// <summary>
/// A business-context role a workflow needs in order to run, fully resolved for one tenant binding.
/// This describes the modeled application's own role, not a FlowOS tenant/IAM role — it is compiled
/// into the runtime <c>WorkflowDefinition</c> as declarative metadata and never written to FlowOS's
/// own Role/TenantUserRole tables. Membership (<see cref="ResolutionType"/>) is resolved fresh, per
/// workflow instance, only once that instance is actually running.
/// </summary>
/// <param name="TemplateRoles">Template role names that resolve to this effective role.</param>
/// <param name="RoleName">Effective business-role name after role overrides.</param>
/// <param name="Capabilities">Effective capability codes after capability overrides.</param>
/// <param name="IsExplicitlyMapped">True when the binding names this role in RoleOverrides.</param>
/// <param name="ResolutionType">"Assignment" | "Expression" | "Static" — see <see cref="RoleBlueprint.ResolutionType"/>.</param>
/// <param name="MemberExpression">Expression resolution: template evaluated against the instance's business payload.</param>
/// <param name="StaticMembers">Static resolution: fixed caller identifiers, after binding overrides.</param>
public sealed record BusinessRoleRequirement(
    IReadOnlyList<string> TemplateRoles,
    string RoleName,
    IReadOnlyList<string> Capabilities,
    bool IsExplicitlyMapped,
    string ResolutionType,
    string? MemberExpression,
    IReadOnlyList<string> StaticMembers);

/// <summary>
/// Resolves the business-context roles and capabilities a context-binding revision requires, and how
/// each one's membership should be determined at instance runtime. Template roles the binding does not
/// explicitly map are free-standing business-context declarations; explicitly mapped roles just rename
/// the effective role so a typo cannot silently point two unrelated template roles at the same name.
/// </summary>
public static class ContextRoleProvisioningRules
{
    private const string DefaultResolutionType = "Assignment";

    public static IReadOnlyList<BusinessRoleRequirement> Resolve(
        WorkflowClassBlueprint blueprint,
        WorkflowContextBindingDefinition definition)
    {
        if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
        if (definition == null) throw new ArgumentNullException(nameof(definition));

        var accumulated = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var role in blueprint.Roles)
        {
            if (role == null || string.IsNullOrWhiteSpace(role.Name)) continue;

            var templateRole = role.Name.Trim();
            var isMapped = definition.RoleOverrides.TryGetValue(templateRole, out var mapped) &&
                           !string.IsNullOrWhiteSpace(mapped);
            var roleName = isMapped ? mapped!.Trim() : templateRole;

            if (!accumulated.TryGetValue(roleName, out var accumulator))
            {
                accumulator = new Accumulator(roleName);
                accumulated[roleName] = accumulator;
                order.Add(roleName);
            }

            var resolutionType = string.IsNullOrWhiteSpace(role.ResolutionType)
                ? DefaultResolutionType
                : role.ResolutionType.Trim();

            var staticMembers = definition.RoleStaticMemberOverrides.TryGetValue(templateRole, out var overrideMembers) &&
                                 overrideMembers != null && overrideMembers.Count > 0
                ? overrideMembers
                : role.StaticMembers;

            accumulator.Add(
                templateRole,
                isMapped,
                ResolveCapabilities(role, definition),
                resolutionType,
                role.MemberExpression,
                staticMembers ?? new List<string>());
        }

        return order
            .Select(roleName => accumulated[roleName].Build())
            .ToList();
    }

    private static IEnumerable<string> ResolveCapabilities(
        RoleBlueprint role,
        WorkflowContextBindingDefinition definition)
    {
        foreach (var capability in role.GrantedCapabilities)
        {
            if (string.IsNullOrWhiteSpace(capability)) continue;

            var code = capability.Trim();
            yield return definition.CapabilityOverrides.TryGetValue(code, out var mapped) &&
                         !string.IsNullOrWhiteSpace(mapped)
                ? mapped.Trim()
                : code;
        }
    }

    private sealed class Accumulator
    {
        private readonly string _roleName;
        private readonly List<string> _templateRoles = new();
        private readonly List<string> _capabilities = new();
        private readonly HashSet<string> _seenCapabilities = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _staticMembers = new();
        private readonly HashSet<string> _seenStaticMembers = new(StringComparer.OrdinalIgnoreCase);
        private bool _isExplicitlyMapped;
        private string _resolutionType = DefaultResolutionType;
        private string? _memberExpression;

        public Accumulator(string roleName) => _roleName = roleName;

        public void Add(
            string templateRole,
            bool isMapped,
            IEnumerable<string> capabilities,
            string resolutionType,
            string? memberExpression,
            IEnumerable<string> staticMembers)
        {
            // The first template role to fold into this effective name sets how membership is
            // resolved; later template roles that happen to map onto the same effective name only
            // contribute capabilities/static members (two template roles disagreeing on
            // ResolutionType for the same effective role is a template authoring error, not
            // something to silently merge).
            if (_templateRoles.Count == 0)
            {
                _resolutionType = resolutionType;
                _memberExpression = memberExpression;
            }

            _templateRoles.Add(templateRole);
            _isExplicitlyMapped |= isMapped;

            foreach (var capability in capabilities)
            {
                if (_seenCapabilities.Add(capability))
                {
                    _capabilities.Add(capability);
                }
            }

            foreach (var member in staticMembers)
            {
                if (string.IsNullOrWhiteSpace(member)) continue;
                var trimmed = member.Trim();
                if (_seenStaticMembers.Add(trimmed))
                {
                    _staticMembers.Add(trimmed);
                }
            }
        }

        public BusinessRoleRequirement Build()
            => new(_templateRoles, _roleName, _capabilities, _isExplicitlyMapped, _resolutionType, _memberExpression, _staticMembers);
    }
}
