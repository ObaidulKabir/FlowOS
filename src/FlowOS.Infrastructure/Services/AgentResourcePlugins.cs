using FlowOS.Application.Common.Interfaces;
using FlowOS.Workflows.Domain;

namespace FlowOS.Infrastructure.Services;

public abstract class CapabilityBackedResourcePlugin : IWorkflowActionPlugin, IAgentResourcePlugin
{
    private readonly ICapabilityInvoker _invoker;

    protected CapabilityBackedResourcePlugin(ICapabilityInvoker invoker)
    {
        _invoker = invoker;
    }

    public abstract string ActionType { get; }
    public string Name => ActionType;
    public abstract string Operation { get; }
    public abstract string Category { get; }
    public abstract string Description { get; }
    public virtual string SideEffect => "none";
    public virtual bool Prefetch => true;

    public WorkflowActionPluginResult BuildMessage(WorkflowActionPluginContext context)
    {
        var payload = WorkflowActionPluginPayloadFactory.BuildPayload(context);
        return new WorkflowActionPluginResult($"WorkflowAction:{ActionType}", payload);
    }

    public async Task<AgentResourceResult> ExecuteAsync(
        AgentResourceRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.CanonicalContext)
            payload[item.Key] = item.Value;
        foreach (var item in request.EventPayloads)
            payload[item.Key] = item.Value;

        var invoked = await _invoker.InvokeAsync(
            request.TenantId,
            request.CapabilityName,
            Operation,
            payload,
            request.WorkflowInstanceId,
            request.StepId,
            cancellationToken);

        return new AgentResourceResult(
            invoked.Ok,
            request.ToolName,
            request.CapabilityName,
            invoked.Parsed ?? invoked.Body,
            invoked.Error);
    }
}

public sealed class LookupRecordResourcePlugin : CapabilityBackedResourcePlugin
{
    public LookupRecordResourcePlugin(ICapabilityInvoker invoker) : base(invoker) { }
    public override string ActionType => "LookupRecord";
    public override string Operation => "lookup";
    public override string Category => "records";
    public override string Description =>
        "Read one tenant record by identifiers from canonical context. FlowOS calls the bound capability; the agent never sees the URL.";
}

public sealed class QueryRecordsResourcePlugin : CapabilityBackedResourcePlugin
{
    public QueryRecordsResourcePlugin(ICapabilityInvoker invoker) : base(invoker) { }
    public override string ActionType => "QueryRecords";
    public override string Operation => "query";
    public override string Category => "records";
    public override string Description =>
        "Query/list tenant records (inventory, jobs, invoices) through a capability binding.";
}

public sealed class FetchDocumentResourcePlugin : CapabilityBackedResourcePlugin
{
    public FetchDocumentResourcePlugin(ICapabilityInvoker invoker) : base(invoker) { }
    public override string ActionType => "FetchDocument";
    public override string Operation => "fetch-document";
    public override string Category => "documents";
    public override string Description =>
        "Fetch a tenant document or attachment metadata/content via a capability binding.";
}

public sealed class SearchKnowledgeResourcePlugin : CapabilityBackedResourcePlugin
{
    public SearchKnowledgeResourcePlugin(ICapabilityInvoker invoker) : base(invoker) { }
    public override string ActionType => "SearchKnowledge";
    public override string Operation => "search";
    public override string Category => "knowledge";
    public override string Description =>
        "Search a tenant knowledge base or policy corpus through a capability binding.";
}

public sealed class CheckPolicyResourcePlugin : CapabilityBackedResourcePlugin
{
    public CheckPolicyResourcePlugin(ICapabilityInvoker invoker) : base(invoker) { }
    public override string ActionType => "CheckPolicy";
    public override string Operation => "check-policy";
    public override string Category => "policy";
    public override string Description =>
        "Read tenant policy limits and rules (approval caps, SLAs, required fields) through a capability binding.";
}
