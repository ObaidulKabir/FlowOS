using FlowOS.MCP.Services;

namespace FlowOS.MCP.UnitTests;

/// <summary>
/// Verifies that the MCP guidance for AI Context accurately describes the
/// event-vs-tool separation introduced by the fix for the events-as-tools bug.
/// Legal next-step events must NOT be described as callable function tools;
/// they are JSON actions listed in the system prompt.
/// </summary>
public class AiContextGuidanceTests
{
    [Fact]
    public void SystemInstructions_clarifies_events_are_json_actions_not_tools()
    {
        var instructions = FlowOsMcpGuidance.SystemInstructions;

        // The key statement that must appear in the system instructions.
        Assert.Contains("NOT exposed as callable function tools", instructions);
        Assert.Contains("JSON actions", instructions);
        // Confirm it references LegalNextStepEvents so the context is clear.
        Assert.Contains("LegalNextStepEvents", instructions);
    }

    [Fact]
    public void BoundedAutonomyTasksGuide_tools_bullet_does_not_say_events_are_tools()
    {
        var guide = FlowOsMcpGuidance.BoundedAutonomyTasksGuide;

        // Must explicitly say events are NOT callable tools.
        Assert.Contains("not** callable function tools", guide);
        // Must say events appear as JSON actions.
        Assert.Contains("JSON actions", guide);
        // Must not claim events are always in the tool list (the old wrong wording).
        Assert.DoesNotContain("legal `nextSteps` events (always)", guide);
    }

    [Fact]
    public void AiTaskAutomationGuide_what_not_to_do_includes_event_tool_antipattern()
    {
        var guide = FlowOsMcpGuidance.AiTaskAutomationGuide;

        // The "What not to do" section must warn against adding events as tools.
        Assert.Contains("legal next-step events", guide);
        Assert.Contains("function tools", guide);
        Assert.Contains("structured-output", guide);
    }

    [Fact]
    public void BoundedAutonomyTasksGuide_tool_bullet_still_mentions_declared_agentTools()
    {
        var guide = FlowOsMcpGuidance.BoundedAutonomyTasksGuide;

        // Declared tools must still be mentioned.
        Assert.Contains("agentTools", guide);
        Assert.Contains("LookupRecord", guide);
        Assert.Contains("CheckPolicy", guide);
    }
}
