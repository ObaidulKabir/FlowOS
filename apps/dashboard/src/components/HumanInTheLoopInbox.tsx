import React, { useEffect, useState, useMemo } from 'react';
import {
  WorkflowInstance,
  WorkflowClass
} from '../types';
import { api } from '../api/client';
import {
  AlertCircle,
  AlertTriangle,
  Bot,
  Check,
  CheckCircle2,
  Clock,
  Copy,
  History,
  Send,
  Sparkles,
  UserCheck,
  X,
  Zap
} from 'lucide-react';

interface Props {
  instances: WorkflowInstance[];
  blueprints?: WorkflowClass[];
  onInspectInstance: (instanceId: string) => void;
  onRefresh: () => Promise<void>;
  role?: 'Tenant' | 'Admin';
}

interface ParsedAgentDecision {
  confidence?: number;
  threshold?: number;
  reason?: string;
  suggestedEvent?: string;
  suggestedPayload?: Record<string, unknown>;
  model?: string;
  provider?: string;
  toolsUsed?: string[];
  raw?: any;
}

const isWaiting = (status: unknown): boolean => {
  if (status === 1 || status === 'Waiting') return true;
  if (typeof status === 'string' && status.toLowerCase().includes('wait')) return true;
  return false;
};

export const HumanInTheLoopInbox: React.FC<Props> = ({
  instances,
  blueprints = [],
  onInspectInstance,
  onRefresh,
  role = 'Tenant'
}) => {
  const waitingInstances = useMemo(
    () => instances.filter(i => isWaiting(i.status)),
    [instances]
  );

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [auditData, setAuditData] = useState<any | null>(null);
  const [loadingAudit, setLoadingAudit] = useState(false);
  const [copiedId, setCopiedId] = useState<string | null>(null);
  const [executingAction, setExecutingAction] = useState(false);
  const [actionSuccess, setActionSuccess] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Custom Event Submission
  const [showCustomModal, setShowCustomModal] = useState(false);
  const [customEvent, setCustomEvent] = useState({ eventType: 'HumanApproved', payload: '{}' });

  useEffect(() => {
    if (!selectedId && waitingInstances.length > 0) {
      const firstId = waitingInstances[0].id || waitingInstances[0].workflowId;
      if (firstId) setSelectedId(firstId);
    }
  }, [waitingInstances, selectedId]);

  useEffect(() => {
    if (!selectedId) {
      setAuditData(null);
      return;
    }

    let isMounted = true;
    setLoadingAudit(true);
    setActionSuccess(null);
    setActionError(null);

    api.getWorkflowAudit(selectedId, role)
      .then(res => {
        if (isMounted) setAuditData(res);
      })
      .catch(err => {
        if (isMounted) setActionError(err.message || 'Failed to load instance telemetry');
      })
      .finally(() => {
        if (isMounted) setLoadingAudit(false);
      });

    return () => {
      isMounted = false;
    };
  }, [selectedId, role]);

  const selectedInstance = useMemo(
    () => waitingInstances.find(i => (i.id || i.workflowId) === selectedId),
    [waitingInstances, selectedId]
  );

  const matchedBlueprint = useMemo(() => {
    if (!selectedInstance) return null;
    return blueprints.find(b =>
      b.id === selectedInstance.workflowClassId ||
      b.name.toLowerCase() === (selectedInstance.workflowClassName || '').toLowerCase()
    );
  }, [blueprints, selectedInstance]);

  // Extract Decision Packet from timeline events
  const parsedDecision: ParsedAgentDecision | null = useMemo(() => {
    if (!auditData) return null;
    const timeline = Array.isArray(auditData.timeline)
      ? auditData.timeline
      : Array.isArray(auditData.Timeline)
      ? auditData.Timeline
      : [];

    // Find the latest agent decision or evaluation event
    for (let i = timeline.length - 1; i >= 0; i--) {
      const evt = timeline[i];
      const type = String(evt.eventType || evt.EventType || '');
      const keyData = evt.keyData || evt.KeyData || {};
      const payloadRaw = keyData.Payload || keyData.payload;

      if (type.includes('Agent') || type.includes('Decision') || type.includes('Insight') || type.includes('Parked') || payloadRaw) {
        let payloadObj: any = null;
        if (typeof payloadRaw === 'string') {
          try {
            payloadObj = JSON.parse(payloadRaw);
          } catch {
            payloadObj = { text: payloadRaw };
          }
        } else if (typeof payloadRaw === 'object' && payloadRaw !== null) {
          payloadObj = payloadRaw;
        }

        if (payloadObj) {
          const confidence = Number(payloadObj.confidence ?? payloadObj.Confidence ?? payloadObj.confidenceScore ?? 0.74);
          const threshold = Number(payloadObj.threshold ?? payloadObj.Threshold ?? payloadObj.autoCommitThreshold ?? 0.85);
          const reason = String(payloadObj.reason ?? payloadObj.Reason ?? payloadObj.rationale ?? payloadObj.explanation ?? evt.summary ?? evt.Summary ?? '');
          const suggestedEvent = String(payloadObj.suggestedEvent ?? payloadObj.SuggestedEvent ?? payloadObj.event ?? 'Approve');
          const suggestedPayload = payloadObj.suggestedPayload ?? payloadObj.SuggestedPayload ?? payloadObj.delta;
          const model = payloadObj.model ?? payloadObj.Model;
          const provider = payloadObj.provider ?? payloadObj.Provider;
          const toolsUsed = Array.isArray(payloadObj.toolsUsed ?? payloadObj.ToolsUsed) ? payloadObj.toolsUsed : [];

          return {
            confidence,
            threshold,
            reason: reason || 'Agent evaluated inputs and generated candidate transition.',
            suggestedEvent,
            suggestedPayload,
            model,
            provider,
            toolsUsed,
            raw: payloadObj
          };
        }
      }
    }

    // Default synthetic decision if instance is parked waiting for human
    return {
      confidence: 0.72,
      threshold: 0.85,
      reason: 'Agent confidence score was below the auto-commit threshold. Step requires manual governance approval.',
      suggestedEvent: 'Approve',
      model: 'tenant-llm-orchestrator'
    };
  }, [auditData]);

  const handleCopy = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  const handleApproveSuggested = async () => {
    if (!selectedId || !parsedDecision?.suggestedEvent) return;
    setExecutingAction(true);
    setActionError(null);
    setActionSuccess(null);
    try {
      await api.publishEvent(
        parsedDecision.suggestedEvent,
        selectedId,
        selectedInstance?.correlationId,
        parsedDecision.suggestedPayload || {},
        role
      );
      setActionSuccess(`Published suggested event '${parsedDecision.suggestedEvent}' successfully!`);
      await onRefresh();
    } catch (err: any) {
      setActionError(err.message || 'Failed to publish event');
    } finally {
      setExecutingAction(false);
    }
  };

  const handleSubmitCustomEvent = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedId || !customEvent.eventType.trim()) return;

    let parsedPayload: any = {};
    if (customEvent.payload.trim()) {
      try {
        parsedPayload = JSON.parse(customEvent.payload);
      } catch (err: any) {
        setActionError(`Invalid JSON in payload: ${err.message}`);
        return;
      }
    }

    setExecutingAction(true);
    setActionError(null);
    setActionSuccess(null);
    try {
      await api.publishEvent(
        customEvent.eventType.trim(),
        selectedId,
        selectedInstance?.correlationId,
        parsedPayload,
        role
      );
      setActionSuccess(`Event '${customEvent.eventType.trim()}' published successfully!`);
      setShowCustomModal(false);
      await onRefresh();
    } catch (err: any) {
      setActionError(err.message || 'Failed to submit event');
    } finally {
      setExecutingAction(false);
    }
  };

  return (
    <div className="space-y-4">
      {/* Top Banner / Explanation */}
      <div className="rounded-2xl border border-amber-500/30 bg-gradient-to-r from-amber-950/40 via-slate-900 to-slate-950 p-4 flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <div className="p-2 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-400">
            <UserCheck size={20} />
          </div>
          <div>
            <h3 className="text-base font-bold text-white flex items-center gap-2">
              Human-in-the-Loop Decision Center
              <span className="px-2 py-0.5 rounded-full text-xs font-mono bg-amber-500/20 text-amber-300 border border-amber-500/40 font-bold">
                {waitingInstances.length} Parked for Review
              </span>
            </h3>
            <p className="text-xs text-slate-400 mt-0.5">
              Instances parked because model confidence was below the auto-commit threshold, or the step requires human approval.
            </p>
          </div>
        </div>
        <button
          onClick={onRefresh}
          className="px-3 py-1.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold border border-slate-700 transition-colors"
        >
          Refresh Queue
        </button>
      </div>

      {actionSuccess && (
        <div className="p-3 bg-emerald-950/50 border border-emerald-500/40 rounded-xl text-xs text-emerald-200 flex items-center gap-2">
          <CheckCircle2 size={14} className="text-emerald-400" />
          <span>{actionSuccess}</span>
        </div>
      )}

      {actionError && (
        <div className="p-3 bg-rose-950/50 border border-rose-500/40 rounded-xl text-xs text-rose-200 flex items-center gap-2">
          <AlertCircle size={14} className="text-rose-400" />
          <span>{actionError}</span>
        </div>
      )}

      {waitingInstances.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/40 p-12 text-center space-y-2">
          <CheckCircle2 size={36} className="mx-auto text-emerald-500/70" />
          <div className="text-base font-bold text-white">All Decisions Approved</div>
          <p className="text-xs text-slate-400 max-w-md mx-auto">
            There are currently no instances waiting for human intervention. High-confidence steps auto-commit automatically according to tenant autonomy policies.
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-[340px_1fr] gap-5">
          {/* Left Column: Waiting Queue */}
          <div className="bg-slate-900 border border-slate-700 rounded-2xl overflow-hidden h-fit">
            <div className="p-3.5 border-b border-slate-800 flex items-center justify-between">
              <span className="text-xs font-bold text-slate-300 uppercase tracking-wider">
                Pending Reviews ({waitingInstances.length})
              </span>
              <span className="text-[11px] text-amber-400 font-medium">Requires Attention</span>
            </div>
            <div className="divide-y divide-slate-800/80 max-h-[600px] overflow-y-auto">
              {waitingInstances.map(inst => {
                const id = inst.id || inst.workflowId || 'unknown';
                const isSelected = selectedId === id;
                const step = inst.currentStepId || inst.currentStep || 'UnknownStep';
                const wfName = inst.workflowClassName || 'Workflow';

                return (
                  <button
                    key={id}
                    onClick={() => setSelectedId(id)}
                    className={`w-full text-left p-3.5 transition-all ${
                      isSelected
                        ? 'bg-amber-500/10 border-l-4 border-amber-500'
                        : 'hover:bg-slate-800/60'
                    }`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-semibold text-white text-xs truncate">{wfName}</span>
                      <span className="px-1.5 py-0.5 rounded font-mono text-[10px] bg-amber-500/20 text-amber-300 border border-amber-500/30 font-semibold shrink-0">
                        {step}
                      </span>
                    </div>
                    <div className="flex items-center justify-between mt-1 text-[11px] text-slate-500">
                      <span className="font-mono">{id.substring(0, 8)}...</span>
                      <span className="flex items-center gap-1">
                        <Clock size={10} />
                        {new Date(inst.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                      </span>
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          {/* Right Column: Decision Packet Inspector & Actions */}
          <div className="space-y-4">
            {loadingAudit ? (
              <div className="bg-slate-900 border border-slate-700 rounded-2xl p-10 text-center text-xs text-slate-400 space-y-2">
                <Sparkles size={24} className="mx-auto text-amber-400 animate-spin" />
                <div>Hydrating Decision Packet & Context...</div>
              </div>
            ) : selectedInstance && parsedDecision ? (
              <div className="bg-slate-900 border border-slate-700 rounded-2xl p-5 space-y-5">
                {/* Instance Top Bar */}
                <div className="flex flex-wrap items-start justify-between gap-3 border-b border-slate-800 pb-4">
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="text-xs font-bold uppercase tracking-wider text-amber-400 flex items-center gap-1">
                        <AlertTriangle size={12} /> Pending Decision
                      </span>
                      <span className="text-slate-600">·</span>
                      <span className="text-xs text-slate-400 font-mono">
                        Instance {selectedId?.substring(0, 12)}...
                      </span>
                      <button
                        onClick={() => selectedId && handleCopy(selectedId, selectedId)}
                        className="p-1 hover:text-white text-slate-500"
                        title="Copy Instance UUID"
                      >
                        {copiedId === selectedId ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
                      </button>
                    </div>
                    <h2 className="text-xl font-bold text-white mt-1 flex flex-wrap items-center gap-2">
                      <span>{matchedBlueprint?.name || selectedInstance.workflowClassName}</span>
                      {matchedBlueprint?.version && (
                        <span className="text-[11px] font-mono text-cyan-300 bg-cyan-950/60 border border-cyan-800/50 px-2 py-0.5 rounded-full font-normal">
                          v{matchedBlueprint.version}
                        </span>
                      )}
                      <span className="text-xs font-mono text-slate-500 font-normal">
                        Step: <code className="text-amber-300 font-bold">{selectedInstance.currentStepId || selectedInstance.currentStep}</code>
                      </span>
                    </h2>
                  </div>

                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => selectedId && onInspectInstance(selectedId)}
                      className="px-3 py-1.5 bg-slate-800 hover:bg-slate-750 border border-slate-700 text-slate-300 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-colors"
                    >
                      <History size={13} /> Full Audit & Replay
                    </button>
                  </div>
                </div>

                {/* Autonomy Confidence Meter */}
                <div className="rounded-xl border border-amber-500/20 bg-amber-950/20 p-4 space-y-2.5">
                  <div className="flex items-center justify-between text-xs">
                    <span className="font-bold text-slate-200 flex items-center gap-1.5">
                      <Bot size={14} className="text-amber-400" /> Model Confidence vs Autonomy Threshold
                    </span>
                    <span className="font-mono text-xs">
                      <strong className="text-amber-300">{Math.round((parsedDecision.confidence ?? 0.72) * 100)}%</strong>
                      <span className="text-slate-500"> / {Math.round((parsedDecision.threshold ?? 0.85) * 100)}% required</span>
                    </span>
                  </div>

                  <div className="w-full bg-slate-800 h-2.5 rounded-full overflow-hidden relative">
                    <div
                      className="bg-gradient-to-r from-amber-500 to-amber-400 h-full rounded-full transition-all"
                      style={{ width: `${Math.min(100, Math.round((parsedDecision.confidence ?? 0.72) * 100))}%` }}
                    />
                    <div
                      className="absolute top-0 bottom-0 w-0.5 bg-rose-400 shadow-md"
                      style={{ left: `${Math.round((parsedDecision.threshold ?? 0.85) * 100)}%` }}
                      title={`Autonomy Gate (${Math.round((parsedDecision.threshold ?? 0.85) * 100)}%)`}
                    />
                  </div>

                  <p className="text-[11px] text-slate-400 leading-relaxed">
                    FlowOS prevented automatic state transition because confidence did not reach the threshold. Human oversight is enforced.
                  </p>
                </div>

                {/* Agent Reasoning */}
                <div className="space-y-1.5">
                  <div className="text-xs font-bold text-slate-300 flex items-center gap-1.5">
                    <Sparkles size={13} className="text-purple-400" />
                    Agent Evaluation & Reasoning
                  </div>
                  <div className="p-3.5 rounded-xl bg-slate-950/80 border border-slate-800 text-xs text-slate-200 leading-relaxed font-sans">
                    {parsedDecision.reason}
                  </div>
                </div>

                {/* Candidate Action / Suggested Event */}
                <div className="rounded-xl border border-slate-800 bg-slate-950/60 p-4 space-y-3">
                  <div className="flex items-center justify-between text-xs">
                    <span className="font-bold text-slate-300 flex items-center gap-1.5">
                      <Zap size={13} className="text-emerald-400" />
                      Candidate Suggested Event: <code className="text-emerald-300 font-mono font-bold text-sm ml-1">{parsedDecision.suggestedEvent}</code>
                    </span>
                  </div>

                  {parsedDecision.suggestedPayload && Object.keys(parsedDecision.suggestedPayload).length > 0 && (
                    <div className="space-y-1">
                      <div className="text-[10px] text-slate-500 font-semibold uppercase tracking-wide">Payload Delta</div>
                      <pre className="p-2.5 rounded-lg bg-slate-900 border border-slate-800 text-[11px] font-mono text-sky-300 overflow-x-auto">
                        {JSON.stringify(parsedDecision.suggestedPayload, null, 2)}
                      </pre>
                    </div>
                  )}

                  {/* Actions Buttons */}
                  <div className="flex flex-wrap items-center gap-2 pt-2 border-t border-slate-800/80">
                    <button
                      onClick={handleApproveSuggested}
                      disabled={executingAction}
                      className="px-4 py-2 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-bold flex items-center gap-1.5 shadow-lg shadow-emerald-600/20 disabled:opacity-60 transition-all"
                    >
                      <Check size={14} />
                      {executingAction ? 'Publishing…' : `Approve & Publish '${parsedDecision.suggestedEvent}'`}
                    </button>

                    <button
                      onClick={() => {
                        setCustomEvent({ eventType: 'Rejected', payload: JSON.stringify({ reason: 'Declined by human operator' }, null, 2) });
                        setShowCustomModal(true);
                      }}
                      disabled={executingAction}
                      className="px-3.5 py-2 rounded-xl bg-rose-600/20 hover:bg-rose-600/30 text-rose-300 border border-rose-500/30 text-xs font-bold flex items-center gap-1.5 disabled:opacity-60 transition-all"
                    >
                      <X size={13} />
                      Reject / Decline
                    </button>

                    <button
                      onClick={() => {
                        setCustomEvent({ eventType: parsedDecision.suggestedEvent || 'HumanApproved', payload: '{}' });
                        setShowCustomModal(true);
                      }}
                      disabled={executingAction}
                      className="px-3.5 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 border border-slate-700 text-xs font-semibold flex items-center gap-1.5 transition-colors ml-auto"
                    >
                      <Send size={13} />
                      Custom Event Override…
                    </button>
                  </div>
                </div>
              </div>
            ) : null}
          </div>
        </div>
      )}

      {/* Custom Event Modal */}
      {showCustomModal && (
        <div className="fixed inset-0 bg-black/75 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <form onSubmit={handleSubmitCustomEvent} className="bg-slate-900 border border-slate-700 rounded-2xl max-w-lg w-full p-5 space-y-4 shadow-2xl">
            <div className="flex items-center justify-between border-b border-slate-800 pb-2">
              <h3 className="text-sm font-bold text-white flex items-center gap-1.5">
                <Send size={14} className="text-blue-400" />
                Publish Manual Workflow Event
              </h3>
              <button
                type="button"
                onClick={() => setShowCustomModal(false)}
                className="text-slate-400 hover:text-white"
              >
                <X size={16} />
              </button>
            </div>

            <div className="space-y-1">
              <label className="text-[11px] font-semibold text-slate-300">Legal Event Type</label>
              <input
                required
                value={customEvent.eventType}
                onChange={e => setCustomEvent({ ...customEvent, eventType: e.target.value })}
                placeholder="e.g. HumanApproved, LoanDeclined, AdditionalDocsRequested"
                className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white font-mono"
              />
            </div>

            <div className="space-y-1">
              <label className="text-[11px] font-semibold text-slate-300">Payload JSON (Optional)</label>
              <textarea
                rows={4}
                value={customEvent.payload}
                onChange={e => setCustomEvent({ ...customEvent, payload: e.target.value })}
                placeholder='{\n  "reviewerComment": "Approved after income verification"\n}'
                className="w-full bg-slate-950 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-sky-300 font-mono resize-y"
              />
            </div>

            <div className="flex gap-2 pt-2">
              <button
                type="button"
                onClick={() => setShowCustomModal(false)}
                className="flex-1 px-3 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={executingAction}
                className="flex-1 px-3 py-2 rounded-lg bg-blue-600 hover:bg-blue-500 text-white text-xs font-bold disabled:opacity-60"
              >
                {executingAction ? 'Publishing…' : 'Publish Event'}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
};
