import React, { useState } from 'react';
import { WorkflowInstance } from '../types';
import { Activity, Copy, Check, Clock, History, X, ShieldAlert, Sparkles } from 'lucide-react';
import { getActiveTenantId } from '../api/client';

interface Props {
  items: WorkflowInstance[];
}

interface AuditTimelineEvent {
  eventId: string;
  eventType: string;
  timestamp: string;
  summary: string;
  keyData?: Record<string, string>;
}

interface AuditDetail {
  id: string;
  definitionName: string;
  version: number;
  currentStepId: string;
  status: string;
  correlationId?: string;
  createdAt: string;
  timeline: AuditTimelineEvent[];
}

export const WorkflowInstanceTable: React.FC<Props> = ({ items }) => {
  const [copiedId, setCopiedId] = useState<string | null>(null);
  const [inspectingInstance, setInspectingInstance] = useState<string | null>(null);
  const [auditDetail, setAuditDetail] = useState<AuditDetail | null>(null);
  const [loadingAudit, setLoadingAudit] = useState(false);
  const [auditError, setAuditError] = useState<string | null>(null);

  const handleCopy = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  const handleInspect = async (instanceId: string) => {
    setInspectingInstance(instanceId);
    setLoadingAudit(true);
    setAuditError(null);
    setAuditDetail(null);

    try {
      const tenantId = getActiveTenantId();
      const res = await fetch(`/api/workflows/${instanceId}/audit`, {
        headers: {
          'x-tenant-id': tenantId,
          'X-Mock-Role': 'Admin'
        }
      });
      if (!res.ok) {
        throw new Error(`Failed to load audit trail (${res.status} ${res.statusText})`);
      }
      const data = await res.json();
      setAuditDetail(data);
    } catch (err: any) {
      setAuditError(err.message || 'Error fetching audit history');
    } finally {
      setLoadingAudit(false);
    }
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
            const status = (item.status === 0 || item.status === 'Running') ? 'Running' :
                           (item.status === 1 || item.status === 'Completed') ? 'Completed' : 'Terminated';

            const statusBadge = status === 'Completed'
              ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30'
              : status === 'Terminated'
              ? 'bg-rose-500/20 text-rose-300 border-rose-500/30'
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

      {/* Live Audit Trail & Telemetry Modal */}
      {inspectingInstance && (
        <div className="fixed inset-0 bg-black/75 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-2xl w-full p-6 shadow-2xl space-y-4 max-h-[85vh] flex flex-col">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <div className="flex items-center gap-2">
                <History className="text-blue-400" size={20} />
                <h3 className="text-base font-bold text-white">
                  Workflow Audit Trail
                </h3>
              </div>
              <button
                onClick={() => setInspectingInstance(null)}
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
                <div className="flex items-center gap-3 pt-1">
                  <span>Class: <strong className="text-white">{auditDetail.definitionName} v{auditDetail.version}</strong></span>
                  <span>•</span>
                  <span>Current Step: <strong className="text-blue-400">{auditDetail.currentStepId}</strong></span>
                  <span>•</span>
                  <span>Status: <strong className="text-emerald-400">{auditDetail.status}</strong></span>
                </div>
              )}
            </div>

            <div className="flex-1 overflow-y-auto space-y-3 pt-2 pr-1">
              {loadingAudit ? (
                <div className="py-12 text-center text-slate-400 text-xs">
                  <div className="animate-spin inline-block w-6 h-6 border-2 border-blue-500 border-t-transparent rounded-full mb-2"></div>
                  <div>Loading immutable event log from database...</div>
                </div>
              ) : auditError ? (
                <div className="p-4 bg-rose-500/10 border border-rose-500/30 rounded-xl text-xs text-rose-300">
                  {auditError}
                </div>
              ) : (!auditDetail?.timeline || auditDetail.timeline.length === 0) ? (
                <div className="py-12 text-center text-slate-500 text-xs italic">
                  No recorded events found in timeline for this instance.
                </div>
              ) : (
                <div className="relative pl-6 border-l border-slate-800 space-y-4">
                  {auditDetail.timeline.map((evt, idx) => {
                    const isInsight = evt.eventType.includes('Insight');
                    const isEscalate = evt.eventType.includes('Escalat') || evt.eventType.includes('Timeout');
                    const isTransition = evt.eventType.includes('Transition');

                    return (
                      <div key={evt.eventId || idx} className="relative group">
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
                            <div className="pt-1.5 flex flex-wrap gap-1.5">
                              {Object.entries(evt.keyData).map(([k, v]) => (
                                <span key={k} className="text-[10px] px-2 py-0.5 rounded bg-slate-900 text-slate-400 border border-slate-800 font-mono">
                                  <strong className="text-slate-300">{k}:</strong> {v}
                                </span>
                              ))}
                            </div>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            <div className="pt-3 border-t border-slate-800 flex justify-end">
              <button
                onClick={() => setInspectingInstance(null)}
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
