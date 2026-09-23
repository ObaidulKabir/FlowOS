import React, { useCallback, useEffect, useState } from 'react';
import { WorkflowInstance, WorkflowClass, TimeTravelSnapshot } from '../types';
import { Activity, Copy, Check, Clock, History, X, ShieldAlert, Sparkles, FileJson, Layers, ChevronDown, ChevronRight, Zap, Rewind } from 'lucide-react';
import { api } from '../api/client';
import { resolveWorkflowInstanceId } from '../audit/resolveWorkflowInstance';
import { WorkflowGraphVisualizer } from './WorkflowGraphVisualizer';
import { WorkflowActionAuditViewer } from './WorkflowActionAuditViewer';
import { TimeTravelPlayer } from './TimeTravelPlayer';

interface Props {
  items: WorkflowInstance[];
  blueprints?: WorkflowClass[];
  inspectInstanceId?: string | null;
  inspectRequestId?: number | null;
  onInspectConsumed?: () => void;
}

const engineStatusLabel = (status: unknown): string => {
  if (status === 0 || status === 'Running') return 'Running';
  if (status === 1 || status === 'Waiting') return 'Waiting';
  if (status === 2 || status === 'Completed') return 'Completed';
  if (status === 3 || status === 'Failed') return 'Failed';
  if (typeof status === 'string' && status.trim()) return status;
  return 'Unknown';
};

interface AuditTimelineEvent {
  eventId: string;
  eventType: string;
  timestamp: string;
  summary: string;
  keyData?: Record<string, string>;
}

interface AuditDetail {
  id: string;
  definitionId?: string;
  definitionName: string;
  version: number;
  currentStepId: string;
  currentState?: string;
  status: string;
  correlationId?: string;
  createdAt: string;
  timeline: AuditTimelineEvent[];
}

const readString = (value: unknown): string => (typeof value === 'string' ? value : '');

const normalizeAuditDetail = (data: any): AuditDetail => {
  const timelineSource = Array.isArray(data?.timeline)
    ? data.timeline
    : Array.isArray(data?.Timeline)
      ? data.Timeline
      : [];

  return {
    id: readString(data?.id || data?.Id),
    definitionId: readString(data?.definitionId || data?.DefinitionId) || undefined,
    definitionName: readString(data?.definitionName || data?.DefinitionName) || 'Unknown',
    version: Number(data?.version ?? data?.Version ?? 0),
    currentStepId: readString(data?.currentStepId || data?.CurrentStepId),
    currentState: readString(data?.currentState || data?.CurrentState) || undefined,
    status: readString(data?.status || data?.Status),
    correlationId: readString(data?.correlationId || data?.CorrelationId) || undefined,
    createdAt: readString(data?.createdAt || data?.CreatedAt),
    timeline: timelineSource.map((evt: any) => ({
      eventId: readString(evt?.eventId || evt?.EventId),
      eventType: readString(evt?.eventType || evt?.EventType),
      timestamp: readString(evt?.timestamp || evt?.Timestamp),
      summary: readString(evt?.summary || evt?.Summary) || 'System event recorded',
      keyData: evt?.keyData || evt?.KeyData || {}
    }))
  };
};

const TimelineEventItem: React.FC<{ evt: AuditTimelineEvent }> = ({ evt }) => {
  const [expanded, setExpanded] = useState(false);
  
  const isInsight = evt.eventType.includes('Insight');
  const isEscalate = evt.eventType.includes('Escalat') || evt.eventType.includes('Timeout');
  const isTransition = evt.eventType.includes('Transition');

  const hasPayload = evt.keyData && evt.keyData.Payload;
  
  return (
    <div className="relative group">
      {/* Timeline Bullet */}
      <div className={`absolute -left-[31px] top-1 w-3.5 h-3.5 rounded-full border-2 border-slate-900 ${
        isInsight 
          ? 'bg-purple-400 ring-2 ring-purple-500/20' 
          : isEscalate 
          ? 'bg-rose-400 ring-2 ring-rose-500/20'
          : isTransition
          ? 'bg-blue-400'
          : 'bg-emerald-400'
      }`} />

      <div className="bg-slate-800/80 border border-slate-700/80 rounded-xl p-3 space-y-1.5 hover:border-slate-600 transition-colors">
        <div className="flex items-center justify-between text-[11px]">
          <div className="font-bold flex items-center gap-1.5 text-white">
            {isInsight ? (
              <span className="text-purple-300 flex items-center gap-1">
                <Sparkles size={12} /> AI Insight Generated
              </span>
            ) : isEscalate ? (
              <span className="text-rose-300 flex items-center gap-1">
                <ShieldAlert size={12} /> Escalation / SLA Timeout
              </span>
            ) : (
              <span className="text-blue-300">{evt.eventType}</span>
            )}
          </div>
          <span className="text-slate-500 flex items-center gap-1 font-mono">
            <Clock size={11} />
            {new Date(evt.timestamp).toLocaleTimeString()}
          </span>
        </div>

        <div className="text-xs text-slate-300 leading-relaxed">
          {evt.summary}
        </div>

        {evt.keyData && Object.keys(evt.keyData).length > 0 && (
          <div className="pt-1.5 space-y-1.5">
            <div className="flex flex-wrap gap-1.5">
              {Object.entries(evt.keyData)
                .filter(([k]) => k !== 'Payload')
                .map(([k, v]) => (
                  <span key={k} className="text-[10px] px-2 py-0.5 rounded bg-slate-900 text-slate-400 border border-slate-800 font-mono">
                    <strong className="text-slate-300">{k}:</strong> {v}
                  </span>
                ))}
            </div>
            {hasPayload && (
              <div className="mt-2 border-t border-slate-700/50 pt-2">
                <button 
                  onClick={() => setExpanded(!expanded)}
                  className="flex items-center gap-1 text-[10px] font-semibold text-emerald-400/90 hover:text-emerald-300 transition-colors bg-emerald-500/10 px-2 py-1 rounded-md border border-emerald-500/20"
                >
                  {expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
                  <FileJson size={12} className="ml-0.5" />
                  {expanded ? 'Hide Payload Data' : 'View Attached Payload Data'}
                </button>
                {expanded && (
                  <div className="mt-2 bg-slate-950/90 border border-slate-800 p-3 rounded-lg font-mono text-[10px] text-emerald-300/90 overflow-x-auto max-h-64 shadow-inner">
                    <pre>{(() => {
                      try {
                        return JSON.stringify(JSON.parse(evt.keyData!.Payload), null, 2);
                      } catch {
                        return evt.keyData!.Payload;
                      }
                    })()}</pre>
                  </div>
                )}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};

export const WorkflowInstanceTable: React.FC<Props> = ({
  items,
  blueprints = [],
  inspectInstanceId = null,
  inspectRequestId = null,
  onInspectConsumed
}) => {
  const [copiedId, setCopiedId] = useState<string | null>(null);
  const [inspectingInstance, setInspectingInstance] = useState<string | null>(null);
  const [auditDetail, setAuditDetail] = useState<AuditDetail | null>(null);
  const [loadingAudit, setLoadingAudit] = useState(false);
  const [auditError, setAuditError] = useState<string | null>(null);
  const [inspectTab, setInspectTab] = useState<'visual' | 'timeline' | 'actions' | 'timetravel'>('visual');
  const [resolvedDefinition, setResolvedDefinition] = useState<any | null>(null);
  const [replaySnapshot, setReplaySnapshot] = useState<TimeTravelSnapshot | null>(null);

  const handleCopy = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  const handleInspect = useCallback(async (instanceId: string) => {
    setInspectingInstance(instanceId);
    setLoadingAudit(true);
    setAuditError(null);
    setAuditDetail(null);
    setInspectTab('timeline');
    setReplaySnapshot(null);

    const matchedInstance = items.find(i =>
      (i.id || i.workflowId) === instanceId || i.correlationId === instanceId
    );
    let bp = blueprints.find(b => 
      b.id === matchedInstance?.workflowClassId || 
      b.name.toLowerCase() === (matchedInstance?.workflowClassName || '').toLowerCase()
    );

    if (bp?.definition) {
      setResolvedDefinition(bp.definition);
    } else {
      setResolvedDefinition(null);
    }

    try {
      const raw = await api.getWorkflowAudit(instanceId);
      const data = normalizeAuditDetail(raw);
      setAuditDetail(data);

      if (!bp?.definition) {
        let foundBp = blueprints.find(b => 
          b.name.toLowerCase() === (data.definitionName || '').toLowerCase() ||
          b.id === data.definitionId
        );

        if (foundBp?.definition) {
          setResolvedDefinition(foundBp.definition);
        } else {
          try {
            const list = await api.list();
            foundBp = list.find(b => 
              b.name.toLowerCase() === (data.definitionName || '').toLowerCase() ||
              b.id === data.definitionId
            );
            if (foundBp?.definition) {
              setResolvedDefinition(foundBp.definition);
            }
          } catch {
            // Ignore fetch error, fallback to synthetic definition
          }
        }

        if (!foundBp?.definition) {
          const stepSet = new Set<string>();
          if (data.currentStepId) stepSet.add(data.currentStepId);
          data.timeline.forEach((t) => {
            if (t.keyData?.CurrentStep) stepSet.add(t.keyData.CurrentStep);
            if (t.keyData?.TargetStep) stepSet.add(t.keyData.TargetStep);
            if (t.keyData?.Step) stepSet.add(t.keyData.Step);
          });
          const stepList = Array.from(stepSet);
          if (stepList.length > 0) {
            setResolvedDefinition({
              workflow: {
                startStepId: stepList[0],
                steps: stepList.map((s, idx) => ({
                  stepId: s,
                  stepType: idx === stepList.length - 1 && data.status === 'Completed' ? 'End' : 'HumanTask',
                  nextSteps: idx < stepList.length - 1 ? { 'Advance': stepList[idx + 1] } : {}
                }))
              },
              stateMachine: {
                entityType: data.definitionName || 'WorkflowProcess',
                initialState: data.currentState || 'Draft',
                states: ['Draft', data.currentState || data.status || 'Active'],
                transitions: []
              }
            });
          }
        }
      }
    } catch (err: any) {
      setAuditError(err.message || 'Error fetching audit history');
    } finally {
      setLoadingAudit(false);
    }
  }, [blueprints, items]);

  useEffect(() => {
    if (!inspectInstanceId) return;
    const targetId = resolveWorkflowInstanceId(items, inspectInstanceId) || inspectInstanceId;
    void handleInspect(targetId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [inspectInstanceId, inspectRequestId]);

  const closeInspect = () => {
    setInspectingInstance(null);
    onInspectConsumed?.();
  };

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-xs text-slate-300">
        <thead className="bg-slate-800/70 text-[11px] text-slate-400 uppercase tracking-wider">
          <tr>
            <th className="py-3 px-4 rounded-l-lg">Instance ID</th>
            <th className="py-3 px-4">WorkflowClass</th>
            <th className="py-3 px-4">Current Step</th>
            <th className="py-3 px-4">Legal State</th>
            <th className="py-3 px-4">Engine Status</th>
            <th className="py-3 px-4">Started</th>
            <th className="py-3 px-4 text-right rounded-r-lg">Audit & Telemetry</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-800">
          {items.map((item, idx) => {
            const id = item.id || item.workflowId || `instance-${idx}`;
            const className = item.workflowClassName || item.workflowClassId || 'Workflow';
            const step = item.currentStepId || item.currentStep || 'Start';
            const state = item.currentState || 'Draft';
            const status = engineStatusLabel(item.status);

            const statusBadge = status === 'Completed'
              ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30'
              : status === 'Failed'
              ? 'bg-rose-500/20 text-rose-300 border-rose-500/30'
              : status === 'Waiting'
              ? 'bg-amber-500/20 text-amber-300 border-amber-500/30'
              : 'bg-blue-500/20 text-blue-300 border-blue-500/30 animate-pulse';

            return (
              <tr key={id} className="hover:bg-slate-800/40 transition-colors">
                <td className="py-3 px-4 font-mono text-slate-400">
                  <div className="flex items-center gap-1.5">
                    <span className="text-white font-medium">{id.substring(0, 8)}...</span>
                    <button
                      onClick={() => handleCopy(id, id)}
                      className="p-1 hover:text-white text-slate-500 rounded transition-colors"
                      title="Copy full UUID"
                    >
                      {copiedId === id ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
                    </button>
                  </div>
                </td>
                <td className="py-3 px-4 font-semibold text-white">{className}</td>
                <td className="py-3 px-4">
                  <span className="px-2 py-0.5 rounded bg-blue-500/10 text-blue-300 border border-blue-500/20 font-mono text-[11px]">
                    {step}
                  </span>
                </td>
                <td className="py-3 px-4">
                  <span className="px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-300 border border-emerald-500/20 font-mono text-[11px]">
                    {state}
                  </span>
                </td>
                <td className="py-3 px-4">
                  <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold border ${statusBadge}`}>
                    {status}
                  </span>
                </td>
                <td className="py-3 px-4 text-slate-400">{new Date(item.createdAt).toLocaleString()}</td>
                <td className="py-3 px-4 text-right">
                  <button
                    onClick={() => handleInspect(id)}
                    className="px-2.5 py-1 bg-blue-600/20 hover:bg-blue-600/30 border border-blue-500/30 text-blue-300 rounded text-xs font-semibold inline-flex items-center gap-1 transition-all"
                  >
                    <History size={13} /> Audit Trail
                  </button>
                </td>
              </tr>
            );
          })}
          {items.length === 0 && (
            <tr>
              <td colSpan={7} className="py-16 text-center text-slate-400">
                <Activity size={32} className="mx-auto mb-2 text-slate-600 opacity-60" />
                <div className="font-semibold text-slate-300">No workflow instances found for this tenant.</div>
                <p className="text-xs text-slate-500 mt-1">
                  Click &quot;Start Live Instance&quot; above, or trigger via the MCP server or REST API.
                </p>
              </td>
            </tr>
          )}
        </tbody>
      </table>

      {/* Live Audit Trail & Visual Execution Modal */}
      {inspectingInstance && (
        <div className="fixed inset-0 bg-black/75 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-6xl w-full p-6 shadow-2xl space-y-4 max-h-[90vh] flex flex-col">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <div className="flex items-center gap-3">
                <div className="flex items-center gap-2">
                  <Activity className="text-blue-400" size={20} />
                  <h3 className="text-base font-bold text-white">
                    Workflow Instance Telemetry
                  </h3>
                </div>

                {/* Tab Switcher */}
                <div className="flex items-center gap-1 bg-slate-950 p-1 rounded-lg border border-slate-800 text-xs">
                  <button
                    onClick={() => setInspectTab('visual')}
                    className={`px-3 py-1 rounded transition-all font-medium flex items-center gap-1.5 ${
                      inspectTab === 'visual' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
                    }`}
                  >
                    <Layers size={13} />
                    Visual Execution Tracer
                  </button>
                  <button
                    onClick={() => setInspectTab('timeline')}
                    className={`px-3 py-1 rounded transition-all font-medium flex items-center gap-1.5 ${
                      inspectTab === 'timeline' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
                    }`}
                  >
                    <History size={13} />
                    Audit Timeline {auditDetail?.timeline?.length ? `(${auditDetail.timeline.length})` : ''}
                  </button>
                  <button
                    onClick={() => setInspectTab('actions')}
                    className={`px-3 py-1 rounded transition-all font-medium flex items-center gap-1.5 ${
                      inspectTab === 'actions' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
                    }`}
                  >
                    <Zap size={13} />
                    Lifecycle Actions Audit
                  </button>
                  <button
                    onClick={() => setInspectTab('timetravel')}
                    className={`px-3 py-1 rounded transition-all font-medium flex items-center gap-1.5 ${
                      inspectTab === 'timetravel' ? 'bg-cyan-600 text-white shadow' : 'text-slate-400 hover:text-white'
                    }`}
                  >
                    <Rewind size={13} />
                    Time-Travel Debugger
                  </button>
                </div>
              </div>

              <button
                onClick={closeInspect}
                className="text-slate-400 hover:text-white p-1 rounded transition-colors"
              >
                <X size={18} />
              </button>
            </div>

            <div className="text-xs text-slate-400 space-y-1">
              <div>
                Instance UUID: <span className="font-mono text-slate-200">{inspectingInstance}</span>
              </div>
              {auditDetail && (
                <div className="flex items-center gap-3 pt-1 flex-wrap">
                  <span>Class: <strong className="text-white">{auditDetail.definitionName} v{auditDetail.version}</strong></span>
                  <span>•</span>
                  <span>Current Step: <strong className="text-amber-400 font-mono">{auditDetail.currentStepId}</strong></span>
                  {auditDetail.currentState && (
                    <>
                      <span>•</span>
                      <span>Legal State: <strong className="text-emerald-300 font-mono">{auditDetail.currentState}</strong></span>
                    </>
                  )}
                  <span>•</span>
                  <span>Status: <strong className="text-emerald-400">{auditDetail.status}</strong></span>
                  {auditDetail.correlationId && auditDetail.correlationId !== inspectingInstance && (
                    <>
                      <span>•</span>
                      <span>Correlation: <strong className="font-mono text-slate-200">{auditDetail.correlationId}</strong></span>
                    </>
                  )}
                </div>
              )}
            </div>

            <div className="flex-1 overflow-y-auto space-y-3 pt-2 pr-1">
              {loadingAudit ? (
                <div className="py-12 text-center text-slate-400 text-xs">
                  <div className="animate-spin inline-block w-6 h-6 border-2 border-blue-500 border-t-transparent rounded-full mb-2"></div>
                  <div>Loading telemetry & event log from kernel...</div>
                </div>
              ) : auditError ? (
                <div className="p-4 bg-rose-500/10 border border-rose-500/30 rounded-xl text-xs text-rose-300">
                  {auditError}
                </div>
              ) : inspectTab === 'visual' ? (
                <div className="py-1">
                  {resolvedDefinition ? (
                    <WorkflowGraphVisualizer
                      definition={resolvedDefinition}
                      currentStepId={replaySnapshot?.toStepId || auditDetail?.currentStepId}
                      currentState={replaySnapshot?.toState || auditDetail?.currentState}
                      completedSteps={Array.from(new Set((auditDetail?.timeline || []).map(e => e.keyData?.CurrentStep || e.keyData?.Step || e.keyData?.TargetStep || '').filter(Boolean)))}
                    />
                  ) : (
                    <div className="py-12 text-center text-slate-500 text-xs italic bg-slate-950/40 rounded-xl border border-slate-800">
                      Resolving definition schema for this instance...
                    </div>
                  )}
                </div>
              ) : inspectTab === 'actions' ? (
                <WorkflowActionAuditViewer workflowInstanceId={inspectingInstance} />
              ) : inspectTab === 'timetravel' ? (
                <TimeTravelPlayer
                  workflowInstanceId={inspectingInstance}
                  definition={resolvedDefinition}
                  onSnapshotChange={setReplaySnapshot}
                />
              ) : (!auditDetail?.timeline || auditDetail.timeline.length === 0) ? (
                <div className="py-12 text-center text-slate-500 text-xs italic">
                  No recorded events found in timeline for this instance.
                </div>
              ) : (
                <div className="relative pl-6 border-l border-slate-800 space-y-4">
                  {auditDetail.timeline.map((evt, idx) => (
                    <TimelineEventItem key={evt.eventId || idx} evt={evt} />
                  ))}
                </div>
              )}
            </div>

            <div className="pt-3 border-t border-slate-800 flex justify-end">
              <button
                onClick={closeInspect}
                className="px-4 py-1.5 bg-slate-800 hover:bg-slate-700 text-white rounded-lg text-xs font-semibold"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
