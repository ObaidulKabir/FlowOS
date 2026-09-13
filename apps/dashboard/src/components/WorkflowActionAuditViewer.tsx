import React, { useState, useEffect } from 'react';
import { Clock, CheckCircle2, XCircle, ChevronDown, ChevronRight, Zap, Globe, Bell, Send, RefreshCw } from 'lucide-react';
import { getActiveTenantId } from '../api/client';

export interface WorkflowActionExecutionLogItem {
  id: string;
  tenantId: string;
  workflowInstanceId: string;
  stepId: string;
  triggerPhase: string;
  actionType: string;
  target?: string;
  status: string;
  executedAtUtc: string;
  durationMs: number;
  httpStatusCode?: number;
  requestPayloadSnippet?: string;
  responseSnippet?: string;
  errorMessage?: string;
  attemptNumber: number;
  outboxMessageId?: string;
}

interface Props {
  workflowInstanceId: string;
}

export const WorkflowActionAuditViewer: React.FC<Props> = ({ workflowInstanceId }) => {
  const [logs, setLogs] = useState<WorkflowActionExecutionLogItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedRow, setExpandedRow] = useState<string | null>(null);
  const [filterType, setFilterType] = useState<string>('all');

  const loadLogs = async () => {
    setLoading(true);
    setError(null);
    try {
      const tenantId = getActiveTenantId();
      const res = await fetch(`/api/workflows/${workflowInstanceId}/actions`, {
        headers: {
          'x-tenant-id': tenantId,
          'X-Mock-Role': 'Admin'
        }
      });
      if (!res.ok) {
        throw new Error(`HTTP ${res.status}: Failed to load lifecycle action audit logs.`);
      }
      const data = await res.json();
      setLogs(Array.isArray(data) ? data : []);
    } catch (err: any) {
      setError(err.message || 'Error loading action history.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadLogs();
  }, [workflowInstanceId]);

  const filteredLogs = logs.filter(item => {
    if (filterType === 'all') return true;
    return item.actionType.toLowerCase() === filterType.toLowerCase();
  });

  const getActionIcon = (type: string) => {
    switch (type.toLowerCase()) {
      case 'webhook':
        return <Globe size={14} className="text-blue-400" />;
      case 'notification':
        return <Bell size={14} className="text-amber-400" />;
      case 'publishevent':
        return <Send size={14} className="text-emerald-400" />;
      default:
        return <Zap size={14} className="text-purple-400" />;
    }
  };

  return (
    <div className="space-y-4">
      {/* Action Controls & Filters */}
      <div className="flex items-center justify-between bg-slate-950/60 p-3 rounded-xl border border-slate-800 text-xs">
        <div className="flex items-center gap-2">
          <span className="text-slate-400 font-medium">Filter Action:</span>
          <select
            value={filterType}
            onChange={(e) => setFilterType(e.target.value)}
            className="bg-slate-900 border border-slate-700 text-slate-200 rounded px-2.5 py-1 text-xs focus:outline-none focus:border-blue-500"
          >
            <option value="all">All Action Types ({logs.length})</option>
            <option value="webhook">Webhooks</option>
            <option value="notification">Notifications</option>
            <option value="publishevent">Events</option>
          </select>
        </div>

        <button
          onClick={loadLogs}
          disabled={loading}
          className="px-2.5 py-1 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded text-xs flex items-center gap-1.5 transition-all"
        >
          <RefreshCw size={12} className={loading ? 'animate-spin' : ''} />
          Refresh Audit Logs
        </button>
      </div>

      {loading && (
        <div className="py-12 text-center text-xs text-slate-400 flex flex-col items-center gap-2">
          <RefreshCw size={20} className="animate-spin text-blue-400" />
          <span>Loading lifecycle action telemetry...</span>
        </div>
      )}

      {error && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 text-rose-300 rounded-lg text-xs">
          {error}
        </div>
      )}

      {!loading && !error && filteredLogs.length === 0 && (
        <div className="py-12 text-center text-slate-400 text-xs bg-slate-950/40 rounded-xl border border-slate-800/80 p-6">
          <Zap size={28} className="mx-auto mb-2 text-slate-600 opacity-60" />
          <div className="font-semibold text-slate-300">No Lifecycle Actions Recorded</div>
          <p className="text-slate-500 mt-1 max-w-sm mx-auto">
            This workflow instance hasn&apos;t triggered any OnEntry or OnExit hooks yet, or matching actions have not executed.
          </p>
        </div>
      )}

      {!loading && !error && filteredLogs.length > 0 && (
        <div className="space-y-2">
          {filteredLogs.map((item) => {
            const isExpanded = expandedRow === item.id;
            const isSuccess = item.status === 'Succeeded';

            return (
              <div
                key={item.id}
                className="bg-slate-950/70 border border-slate-800 rounded-xl overflow-hidden hover:border-slate-700 transition-colors"
              >
                <div
                  onClick={() => setExpandedRow(isExpanded ? null : item.id)}
                  className="p-3.5 flex items-center justify-between cursor-pointer select-none text-xs gap-3"
                >
                  <div className="flex items-center gap-2.5 min-w-0">
                    <span className="text-slate-500 hover:text-slate-300 transition-colors">
                      {isExpanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                    </span>

                    <span className="p-1 rounded bg-slate-900 border border-slate-800">
                      {getActionIcon(item.actionType)}
                    </span>

                    <span className="px-2 py-0.5 rounded text-[11px] font-mono font-semibold bg-slate-900 text-slate-300 border border-slate-800">
                      {item.stepId}
                    </span>

                    <span className="px-1.5 py-0.5 rounded text-[10px] uppercase font-bold tracking-wider bg-blue-950/80 text-blue-300 border border-blue-800/50">
                      {item.triggerPhase}
                    </span>

                    <span className="text-slate-300 truncate font-mono text-[11px]">
                      {item.target || item.actionType}
                    </span>
                  </div>

                  <div className="flex items-center gap-3 shrink-0">
                    {item.httpStatusCode && (
                      <span className={`px-2 py-0.5 rounded font-mono font-bold text-[10px] ${
                        item.httpStatusCode < 400
                          ? 'bg-emerald-950/80 text-emerald-400 border border-emerald-800/60'
                          : 'bg-rose-950/80 text-rose-400 border border-rose-800/60'
                      }`}>
                        HTTP {item.httpStatusCode}
                      </span>
                    )}

                    <span className="flex items-center gap-1 text-slate-400 font-mono text-[11px]">
                      <Clock size={11} className="text-slate-500" />
                      {item.durationMs} ms
                    </span>

                    <span className="flex items-center gap-1 font-semibold text-[11px]">
                      {isSuccess ? (
                        <span className="text-emerald-400 flex items-center gap-1">
                          <CheckCircle2 size={13} /> Succeeded
                        </span>
                      ) : (
                        <span className="text-rose-400 flex items-center gap-1">
                          <XCircle size={13} /> Failed
                        </span>
                      )}
                    </span>
                  </div>
                </div>

                {/* Expanded Details Drawer */}
                {isExpanded && (
                  <div className="border-t border-slate-800/80 bg-slate-900/40 p-4 space-y-3 text-xs">
                    <div className="grid grid-cols-2 md:grid-cols-4 gap-3 text-[11px]">
                      <div>
                        <span className="text-slate-500 block">Executed At</span>
                        <span className="text-slate-200 font-mono">{new Date(item.executedAtUtc).toLocaleString()}</span>
                      </div>
                      <div>
                        <span className="text-slate-500 block">Attempt</span>
                        <span className="text-slate-200 font-mono">#{item.attemptNumber}</span>
                      </div>
                      <div>
                        <span className="text-slate-500 block">Execution UUID</span>
                        <span className="text-slate-400 font-mono truncate block" title={item.id}>{item.id}</span>
                      </div>
                      <div>
                        <span className="text-slate-500 block">Outbox Message ID</span>
                        <span className="text-slate-400 font-mono truncate block" title={item.outboxMessageId || 'N/A'}>
                          {item.outboxMessageId || 'Direct'}
                        </span>
                      </div>
                    </div>

                    {item.errorMessage && (
                      <div className="p-2.5 bg-rose-500/10 border border-rose-500/20 rounded-lg text-rose-300 font-mono text-[11px]">
                        <strong>Error: </strong> {item.errorMessage}
                      </div>
                    )}

                    {item.requestPayloadSnippet && (
                      <div className="space-y-1">
                        <span className="text-slate-400 font-medium text-[11px]">Request Payload Snippet:</span>
                        <pre className="p-2.5 bg-slate-950 rounded-lg border border-slate-800 text-slate-300 font-mono text-[11px] overflow-x-auto max-h-40">
                          {item.requestPayloadSnippet}
                        </pre>
                      </div>
                    )}

                    {item.responseSnippet && (
                      <div className="space-y-1">
                        <span className="text-slate-400 font-medium text-[11px]">Response / Result Snippet:</span>
                        <pre className="p-2.5 bg-slate-950 rounded-lg border border-slate-800 text-slate-300 font-mono text-[11px] overflow-x-auto max-h-40">
                          {item.responseSnippet}
                        </pre>
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
