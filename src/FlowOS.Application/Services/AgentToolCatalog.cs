using FlowOS.Agents.Abstractions;

namespace FlowOS.Application.Services;

public static class AgentToolCatalog
{
    private static readonly HashSet<string> ResourcePlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "LookupRecord", "QueryRecords", "FetchDocument", "SearchKnowledge", "CheckPolicy"
    };

    private static readonly HashSet<string> NotifyPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "Webhook", "Email", "Slack", "WhatsApp", "Notification", "PublishEvent"
    };

    public static IReadOnlyList<AgentToolDescriptor> FromStep(
        IEnumerable<string> legalNextStepEvents,
        IEnumerable<string>? declaredTools,
        IReadOnlyDictionary<string, string>? actionBindings = null)
    {
        var tools = new List<AgentToolDescriptor>();

        foreach (var eventType in legalNextStepEvents)
        {
            tools.Add(new AgentToolDescriptor(
                eventType,
                "event",
                null,
                "Suggest this legal nextSteps event. FlowOS publishes it only if autoCommit allows.",
                "none",
                false,
                null));
        }

        foreach (var raw in declaredTools ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parsed = Parse(raw.Trim());
            if (tools.Any(tool => string.Equals(tool.Name, parsed.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (parsed.Kind == "plugin" &&
                string.IsNullOrWhiteSpace(parsed.Provider) &&
                actionBindings != null)
            {
                if (!actionBindings.TryGetValue(parsed.Name, out var mapped))
                    actionBindings.TryGetValue(StripDeclaredPrefix(parsed.Name), out mapped);
                parsed = parsed with { Provider = mapped };
            }

            tools.Add(parsed);
        }

        return tools;
    }

    public static string ResourcePluginName(string toolName)
    {
        var parsed = Parse(toolName);
        return parsed.Provider ?? parsed.Name;
    }

    public static AgentToolDescriptor Parse(string name)
    {
        var original = name.Trim();
        var (head, tail) = Split(original);

        if (string.Equals(head, "connector", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(head, "capability", StringComparison.OrdinalIgnoreCase))
        {
            var connector = string.IsNullOrWhiteSpace(tail) ? original : tail;
            var read = LooksLikeRead(connector);
            return new AgentToolDescriptor(
                original,
                "capability",
                "InvokeCapability",
                read
                    ? "Read tenant resource via a connector. FlowOS invokes the endpoint; the agent does not call HTTP."
                    : "Write through a tenant connector. Not prefetched at decision time; FlowOS may invoke only if later policy allows.",
                read ? "none" : "write",
                read,
                connector);
        }

        if (ResourcePlugins.Contains(head))
        {
            return new AgentToolDescriptor(
                original,
                "resource",
                head,
                $"{head} tenant resource through a connector. The model never sees the URL.",
                "none",
                !string.IsNullOrWhiteSpace(tail),
                string.IsNullOrWhiteSpace(tail) ? null : tail);
        }

        var pluginName = StripDeclaredPrefix(original);
        var notify = NotifyPlugins.Contains(pluginName);
        return new AgentToolDescriptor(
            original,
            "plugin",
            notify ? pluginName : null,
            notify
                ? "Lifecycle/notify plugin. Not prefetched during agent decision."
                : "Declarative action plugin. FlowOS resolves the tenant plugin binding and runs the registered provider.",
            notify ? "notify" : "write",
            false,
            null);
    }

    private static readonly HashSet<string> MutationVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "create", "update", "delete", "remove", "patch", "modify", "insert", "add",
        "drop", "cancel", "void", "approve", "reject", "escalate", "submit",
        "execute", "run", "dispatch", "trigger", "publish", "send", "notify", "write"
    };

    private static readonly HashSet<string> ReadVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "read", "lookup", "query", "search", "fetch", "check", "find",
        "list", "info", "status", "view", "document", "documents", "policy",
        "policies", "knowledge", "kb", "records", "preview", "audit"
    };

    private static bool LooksLikeRead(string capability)
    {
        if (string.IsNullOrWhiteSpace(capability))
            return false;

        var value = capability.ToLowerInvariant();

        // 1. If explicitly tagged or prefixed
        if (value.EndsWith(":read") || value.StartsWith("read:"))
            return true;
        if (value.EndsWith(":write") || value.StartsWith("write:"))
            return false;

        // 2. Tokenize by separators: '.', ':', '/', '-', '_'
        var tokens = value.Split(new[] { '.', ':', '/', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

        // 3. If any token is an explicit mutation/write verb, fail closed (it is a write)
        if (tokens.Any(token => MutationVerbs.Contains(token)))
            return false;

        // 4. If any token matches a known read verb or domain entity accessor
        if (tokens.Any(token => ReadVerbs.Contains(token)))
            return true;

        // 5. Fallback heuristics for prefixes like kb. or doc.
        return value.Contains("lookup")
            || value.Contains("query")
            || value.Contains("search")
            || value.Contains("fetch")
            || value.Contains("kb.")
            || value.Contains("doc.");
    }

    private static (string Head, string Tail) Split(string name)
    {
        var colon = name.IndexOf(':');
        var dot = name.StartsWith("plugin.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("connector.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("capability.", StringComparison.OrdinalIgnoreCase)
            ? name.IndexOf('.')
            : -1;
        var idx = colon >= 0 ? colon : dot;
        if (idx <= 0 || idx >= name.Length - 1)
            return (name, string.Empty);
        return (name[..idx], name[(idx + 1)..]);
    }

    private static string StripDeclaredPrefix(string name)
    {
        foreach (var prefix in new[] { "plugin:", "plugin.", "connector:", "connector.", "capability:", "capability." })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
                return name[prefix.Length..];
        }

        return name;
    }
}
