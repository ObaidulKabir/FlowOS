using System.Collections.Generic;
using System.Linq;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities.Orchestration;
using FlowOS.Domain.Services;
using FlowOS.Domain.Validation;
using Xunit;

namespace FlowOS.UnitTests.Domain;

/// <summary>
/// ADR-007 §8: structural validation of AgentTeamBlueprint, ahead of any AgentTeamClass
/// persistence. Mirrors the AgentWorkflowBlueprintValidator test style: pin each guard to
/// its error code.
/// </summary>
public class AgentTeamBlueprintValidatorTests
{
    private static readonly AgentTeamBlueprintValidator Validator = new();

    private static AgentTeamBlueprint ValidTeam() => new()
    {
        Name = "RiskAssessmentTeam",
        Objective = "Assess quarterly portfolio risk",
        MaxAutonomyLevel = AgentAutonomyLevel.Supervised.ToString(),
        Slots = new List<AgentTeamSlotBlueprint>
        {
            new() { RoleName = "RiskAnalyst", RequiredCapabilities = new List<string> { "ticket-system.search" } },
        },
    };

    private static void AssertHasError(ValidationResult result, string code) =>
        Assert.Contains(code, result.Errors.Select(e => e.Code));

    private static void AssertNoError(ValidationResult result, string code) =>
        Assert.DoesNotContain(code, result.Errors.Select(e => e.Code));

    private static string Describe(ValidationResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}"));

    [Fact]
    public void ValidTeam_IsAccepted()
    {
        var result = Validator.Validate(ValidTeam());
        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void NullBlueprint_AddsStr000()
    {
        AssertHasError(Validator.Validate(null), "ATM-STR-000");
    }

    [Fact]
    public void MissingName_AddsStr001()
    {
        var team = ValidTeam() with { Name = "" };
        AssertHasError(Validator.Validate(team), "ATM-STR-001");
    }

    [Fact]
    public void MissingObjective_AddsStr002()
    {
        var team = ValidTeam() with { Objective = "" };
        AssertHasError(Validator.Validate(team), "ATM-STR-002");
    }

    [Fact]
    public void UnknownAutonomyLevel_AddsStr003()
    {
        var team = ValidTeam() with { MaxAutonomyLevel = "Omniscient" };
        AssertHasError(Validator.Validate(team), "ATM-STR-003");
    }

    [Fact]
    public void NullAutonomyLevel_IsAccepted()
    {
        var team = ValidTeam() with { MaxAutonomyLevel = null };
        AssertNoError(Validator.Validate(team), "ATM-STR-003");
    }

    [Fact]
    public void NoSlots_AddsStr004()
    {
        var team = ValidTeam() with { Slots = new List<AgentTeamSlotBlueprint>() };
        AssertHasError(Validator.Validate(team), "ATM-STR-004");
    }

    [Fact]
    public void SlotWithEmptyRoleName_AddsSlot001()
    {
        var team = ValidTeam() with
        {
            Slots = new List<AgentTeamSlotBlueprint> { new() { RoleName = "" } },
        };

        AssertHasError(Validator.Validate(team), "ATM-SLOT-001");
    }

    [Fact]
    public void SlotWithEmptyRequiredCapability_AddsSlot002()
    {
        var team = ValidTeam() with
        {
            Slots = new List<AgentTeamSlotBlueprint>
            {
                new() { RoleName = "RiskAnalyst", RequiredCapabilities = new List<string> { "" } },
            },
        };

        AssertHasError(Validator.Validate(team), "ATM-SLOT-002");
    }

    [Fact]
    public void SlotWithBothDefaultAliases_AddsSlot003()
    {
        var team = ValidTeam() with
        {
            Slots = new List<AgentTeamSlotBlueprint>
            {
                new()
                {
                    RoleName = "RiskAnalyst",
                    DefaultDefinitionAlias = "RiskAnalystDefinition",
                    DefaultProfileAlias = "risk-analyst-profile",
                },
            },
        };

        AssertHasError(Validator.Validate(team), "ATM-SLOT-003");
    }

    [Fact]
    public void SlotWithOnlyDefaultDefinitionAlias_IsAccepted()
    {
        var team = ValidTeam() with
        {
            Slots = new List<AgentTeamSlotBlueprint>
            {
                new() { RoleName = "RiskAnalyst", DefaultDefinitionAlias = "RiskAnalystDefinition" },
            },
        };

        AssertNoError(Validator.Validate(team), "ATM-SLOT-003");
    }

    [Fact]
    public void DuplicateSlotRoleName_AddsSlot004()
    {
        var team = ValidTeam() with
        {
            Slots = new List<AgentTeamSlotBlueprint>
            {
                new() { RoleName = "RiskAnalyst" },
                new() { RoleName = "riskanalyst" }, // case-insensitive duplicate
            },
        };

        AssertHasError(Validator.Validate(team), "ATM-SLOT-004");
    }
}
