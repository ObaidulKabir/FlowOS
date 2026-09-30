using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Domain.Entities;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Services;

public sealed class DecisionPacketBuilder : IDecisionPacketBuilder
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPluginBindingRegistryService? _pluginBindings;
    private readonly IFlowOsHostedLlmRuntime? _hosted;

    public DecisionPacketBuilder(
        IUnitOfWork unitOfWork,
        IPluginBindingRegistryService? pluginBindings = null,
        IFlowOsHostedLlmRuntime? hosted = null)
    {
        _unitOfWork = unitOfWork;
        _pluginBindings = pluginBindings;
        _hosted = hosted;
    }

    public async Task<DecisionPacket?> BuildAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? objective = null,
        CancellationToken cancellationToken = default)
    {
        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsNoTrackingAsync(workflowInstanceId, tenantId, cancellationToken);
        if (instance == null) return null;

        var definition = await _unitOfWork.WorkflowDefinitions
            .GetByIdAsNoTrackingAsync(instance.WorkflowDefinitionId, cancellationToken);
        if (definition == null) return null;

        StateMachineDefinition? stateMachine = null;
        if (definition.StateMachineDefinitionId.HasValue)
        {
            stateMachine = await _unitOfWork.StateMachines
                .GetByIdAsNoTrackingAsync(definition.StateMachineDefinitionId.Value, cancellationToken);
        }

        var events = await _unitOfWork.Events.ListByCorrelationIdAsync(workflowInstanceId, cancellationToken);
        var snapshot = await _unitOfWork.WorkflowContextSnapshots
            .GetAsNoTrackingAsync(workflowInstanceId, tenantId, cancellationToken);

        string? policyGuideline = null;
        if (definition.ContextBindingRevisionId.HasValue)
        {
            var revision = await _unitOfWork.WorkflowContextBindings
                .GetRevisionByIdAsNoTrackingAsync(definition.ContextBindingRevisionId.Value, cancellationToken);
            policyGuideline = revision?.Definition.PolicyGuideline;
        }

        var packet = DecisionPacketFactory.Create(
            instance,
            definition,
            stateMachine,
            events,
            snapshot?.CanonicalData,
            policyGuideline,
            objective);

        var step = definition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
        return await AttachBindingsAsync(packet, tenantId, step, cancellationToken);
    }

    public async Task<DecisionPacket?> PreviewAsync(
        Guid tenantId,
        Guid workflowClassId,
        string stepId,
        Guid? contextBindingId = null,
        IReadOnlyDictionary<string, object?>? canonicalSample = null,
        string? currentState = null,
        string? objective = null,
        CancellationToken cancellationToken = default)
    {
        var workflowClass = await _unitOfWork.WorkflowClasses
            .GetByIdAsNoTrackingAsync(workflowClassId, cancellationToken);
        if (workflowClass == null) return null;
        if (!IsVisibleToTenant(workflowClass, tenantId)) return null;

        string? policyGuideline = null;
        if (contextBindingId.HasValue)
        {
            var binding = await _unitOfWork.WorkflowContextBindings
                .GetByIdAsNoTrackingAsync(contextBindingId.Value, tenantId, cancellationToken);
            if (binding == null) return null;

            var revisionId = binding.DraftRevisionId ?? binding.ActiveRevisionId;
            if (revisionId.HasValue)
            {
                var revision = await _unitOfWork.WorkflowContextBindings
                    .GetRevisionByIdAsNoTrackingAsync(revisionId.Value, cancellationToken);
                policyGuideline = revision?.Definition.PolicyGuideline;
            }
        }

        var packet = DecisionPacketFactory.PreviewFromClass(
            workflowClass,
            stepId,
            policyGuideline,
            canonicalSample,
            currentState,
            objective);

        var definition = WorkflowClassCompiler.MapToRuntimeDefinition(workflowClass);
        var step = definition.Steps.FirstOrDefault(s =>
            string.Equals(s.StepId, stepId, StringComparison.OrdinalIgnoreCase));
        return await AttachBindingsAsync(packet, tenantId, step, cancellationToken);
    }

    private async Task<DecisionPacket> AttachBindingsAsync(
        DecisionPacket packet,
        Guid tenantId,
        WorkflowStepDefinition? step,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string>? actionBindings = null;
        var providerAlias = step?.AgentProvider;
        AgentProviderRef? provider = string.Equals(
            providerAlias,
            AgentProviderKinds.FlowosRisk,
            StringComparison.OrdinalIgnoreCase)
            ? new AgentProviderRef(
                AgentProviderKinds.FlowosRisk,
                AgentProviderKinds.FlowosRisk,
                null,
                null,
                false)
            : null;
        AgentPromptRef? promptBinding = null;
        var autoCommit = packet.AutoCommit;
        var toolNames = new List<string>(step?.AgentTools ?? Enumerable.Empty<string>());
        string? effectiveProviderAlias = providerAlias;
        string? effectivePromptAlias = step?.AgentPrompt;

        IReadOnlyList<PluginBindingDto>? registeredTools = null;
        if (_pluginBindings != null)
        {
            actionBindings = await _pluginBindings.ResolveBindingsAsync(
                tenantId, PluginBindingTypes.Action, cancellationToken);
            registeredTools = await _pluginBindings.ListAsync(
                tenantId, PluginBindingTypes.Action, enabledOnly: true, ct: cancellationToken);

            AgentProfilePublicSettings? profile = null;
            if (!string.IsNullOrWhiteSpace(providerAlias) &&
                !AgentProviderKinds.IsFlowOsHosted(providerAlias) &&
                !string.Equals(providerAlias, AgentProviderKinds.FlowosRisk, StringComparison.OrdinalIgnoreCase))
            {
                var profileBinding = await _pluginBindings.GetAgentProfileAsync(
                    tenantId, providerAlias, cancellationToken);
                profile = profileBinding?.Configuration as AgentProfilePublicSettings;
            }

            if (profile == null)
            {
                var rolesToCheck = new List<string>();
                if (step?.AllowedRoles is { Count: > 0 }) rolesToCheck.AddRange(step.AllowedRoles);
                if (packet.AllowedRoles is { Count: > 0 })
                {
                    foreach (var r in packet.AllowedRoles)
                    {
                        if (!rolesToCheck.Contains(r, StringComparer.OrdinalIgnoreCase))
                            rolesToCheck.Add(r);
                    }
                }

                if (rolesToCheck.Count > 0)
                {
                    foreach (var role in rolesToCheck)
                    {
                        var profileBinding = await _pluginBindings.GetAgentProfileAsync(
                            tenantId, role, cancellationToken);
                        if (profileBinding?.Configuration is AgentProfilePublicSettings foundProfile)
                        {
                            profile = foundProfile;
                            break;
                        }
                    }
                }
            }

            if (profile != null)
            {
                if (!string.IsNullOrWhiteSpace(profile.ProviderAlias))
                {
                    effectiveProviderAlias = profile.ProviderAlias;
                }

                if (string.IsNullOrWhiteSpace(effectivePromptAlias) && !string.IsNullOrWhiteSpace(profile.PromptAlias))
                {
                    effectivePromptAlias = profile.PromptAlias;
                }

                if (profile.ToolAliases is { Count: > 0 })
                {
                    foreach (var t in profile.ToolAliases)
                    {
                        if (!string.IsNullOrWhiteSpace(t) && !toolNames.Contains(t, StringComparer.OrdinalIgnoreCase))
                        {
                            toolNames.Add(t.Trim());
                        }
                    }
                }

                if (autoCommit == null && profile.AutoCommitThreshold.HasValue)
                {
                    autoCommit = new AutoCommitPolicy(
                        profile.AutoCommitThreshold.Value,
                        profile.AllowedEvents is { Count: > 0 } ? profile.AllowedEvents : packet.LegalNextStepEvents);
                }
            }

            if (provider == null &&
                string.Equals(effectiveProviderAlias, AgentProviderKinds.FlowosRisk, StringComparison.OrdinalIgnoreCase))
            {
                provider = new AgentProviderRef(
                    AgentProviderKinds.FlowosRisk,
                    AgentProviderKinds.FlowosRisk,
                    null,
                    null,
                    false);
            }

            if (provider == null &&
                !string.IsNullOrWhiteSpace(effectiveProviderAlias) &&
                !AgentProviderKinds.IsFlowOsHosted(effectiveProviderAlias))
            {
                var binding = await _pluginBindings.GetEnabledAsync(
                    tenantId, PluginBindingTypes.Agent, effectiveProviderAlias, cancellationToken);
                if (binding != null && !AgentProviderKinds.IsFlowOsHosted(binding.ProviderName))
                {
                    var settings = binding.Configuration as AgentProviderPublicSettings;
                    provider = new AgentProviderRef(
                        binding.SourceName,
                        binding.ProviderName,
                        settings?.Model,
                        settings?.Endpoint,
                        settings?.HasApiKey ?? false);
                }
            }

            if (provider == null &&
                (string.IsNullOrWhiteSpace(effectiveProviderAlias) || string.Equals(effectiveProviderAlias, "default", StringComparison.OrdinalIgnoreCase)))
            {
                var defaultBinding = await _pluginBindings.GetDefaultAgentProviderAsync(tenantId, cancellationToken);
                if (defaultBinding != null && !AgentProviderKinds.IsFlowOsHosted(defaultBinding.ProviderName))
                {
                    var settings = defaultBinding.Configuration as AgentProviderPublicSettings;
                    provider = new AgentProviderRef(
                        defaultBinding.SourceName,
                        defaultBinding.ProviderName,
                        settings?.Model,
                        settings?.Endpoint,
                        settings?.HasApiKey ?? false);
                }
            }

            if (!string.IsNullOrWhiteSpace(effectivePromptAlias))
            {
                var binding = await _pluginBindings.GetEnabledAsync(
                    tenantId, PluginBindingTypes.Prompt, effectivePromptAlias, cancellationToken);
                if (binding != null)
                {
                    var prompt = binding.Configuration as AgentPromptConfiguration
                        ?? AgentPromptConfiguration.Public(null);
                    promptBinding = new AgentPromptRef(
                        binding.SourceName,
                        prompt.Title,
                        prompt.System,
                        prompt.Body);
                }
            }
        }

        if (provider == null && ShouldAttachHostedProvider(effectiveProviderAlias ?? step?.AgentProvider))
        {
            var hosted = _hosted!.PublicSettings;
            var aliasName = effectiveProviderAlias ?? step?.AgentProvider;
            provider = new AgentProviderRef(
                string.IsNullOrWhiteSpace(aliasName) ? AgentProviderKinds.FlowosHosted : aliasName,
                AgentProviderKinds.FlowosHosted,
                hosted.Model,
                hosted.Endpoint,
                hosted.HasApiKey);
        }

        var tools = AgentToolCatalog.FromStep(
            packet.LegalNextStepEvents,
            toolNames,
            actionBindings,
            registeredTools);

        return packet with { Provider = provider, Tools = tools, PromptBinding = promptBinding, AutoCommit = autoCommit };
    }

    private bool ShouldAttachHostedProvider(string? agentProvider)
    {
        if (_hosted == null)
            return false;
        if (AgentProviderKinds.IsFlowOsHosted(agentProvider))
            return true;
        return string.IsNullOrWhiteSpace(agentProvider) && _hosted.IsConfigured;
    }

    private static bool IsVisibleToTenant(WorkflowClass workflowClass, Guid tenantId) =>
        workflowClass.TenantId == tenantId
        || workflowClass.Scope == FlowOS.Domain.Enums.WorkflowClassScope.Public
        || workflowClass.Status == FlowOS.Domain.Enums.WorkflowClassStatus.Public;
}
