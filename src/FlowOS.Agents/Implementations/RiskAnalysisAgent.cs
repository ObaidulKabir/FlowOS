using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Agents.Implementations;

/// <summary>
/// A rule-based agent that analyzes expense data and suggests actions.
/// </summary>
public class RiskAnalysisAgent : IWorkflowAgent
{
    public Task<AgentResult> ExecuteAsync(AgentContext context)
    {
        // 1. Extract Data
        var payload = context.EntitySnapshot as Dictionary<string, object>;
        if (payload == null)
            return Task.FromResult(AgentResult.Failure("Invalid entity snapshot"));

        // 2. Analyze
        var amount = 0.0;
        if (payload.TryGetValue("Amount", out var amtObj))
            double.TryParse(amtObj.ToString(), out amount);

        var category = payload.ContainsKey("Category") ? payload["Category"].ToString() : "";

        var actions = new List<SuggestedAction>();
        var insight = "Analysis Complete.";

        // 3. Rule Logic
        if (amount > 5000)
        {
            insight = "High value expense detected. Risk Level: High.";
            actions.Add(new SuggestedAction(
                "EVT-ESCALATE",
                "Amount exceeds $5000 threshold.",
                0.95,
                new Dictionary<string, object> { { "RiskScore", 90 } }
            ));
        }
        else if (category == "Office Supplies" && amount < 100)
        {
            insight = "Low risk office supply purchase.";
            actions.Add(new SuggestedAction(
                "EVT-APPROVE",
                "Low value office supplies are pre-approved.",
                0.99,
                new Dictionary<string, object> { { "RiskScore", 10 } }
            ));
        }
        else
        {
            insight = "Standard expense requiring review.";
            // No auto-action suggested, just insight
        }

        // 4. Enrich insight with objective if provided
        if (!string.IsNullOrWhiteSpace(context.Objective))
        {
            insight = $"{context.Objective}: {insight}";
        }

        // 5. Resubmission detection using full event history
        var resubmissionCount = context.EventHistory?.Count(e => e.EventType?.Contains("Submit") == true) ?? 0;
        if (resubmissionCount > 1)
        {
            insight += $" Resubmitted {resubmissionCount} times – elevated risk.";
        }

        return Task.FromResult(AgentResult.WithActions(insight, actions));
    }
}
