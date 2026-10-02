import React, { useEffect, useState } from 'react';
import { api } from '../api/client';
import { Activity, Clock, FileText, CheckCircle, XCircle, Wrench, AlertTriangle, Cpu, Bot } from 'lucide-react';

export const AgentObservabilityDashboard: React.FC = () => {
  const [history, setHistory] = useState<any[]>([]);
  const [metrics, setMetrics] = useState<any>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const toUtc = new Date();
      const fromUtc = new Date(toUtc.getTime() - 7 * 24 * 60 * 60 * 1000); // last 7 days
      
      const [histData, metricData] = await Promise.all([
        api.getAgentHistory(50),
        api.getAgentEvaluationMetrics(fromUtc.toISOString(), toUtc.toISOString())
      ]);
      
      setHistory(histData || []);
      setMetrics(metricData);
    } catch (err: any) {
      setError(err.message || 'Failed to load observability data');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, []);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-bold text-slate-200 flex items-center gap-2">
          <Activity size={16} className="text-violet-400" />
          Agent Observability Dashboard
        </h3>
        <button
          onClick={loadData}
          className="px-3 py-1 bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs rounded-lg transition-colors"
        >
          {loading ? 'Refreshing...' : 'Refresh'}
        </button>
      </div>

      {error && (
        <div className="p-3 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300 flex items-center gap-2">
          <AlertTriangle size={14} />
          {error}
        </div>
      )}

      {/* Metrics Summary */}
      {metrics && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
          <div className="bg-slate-900/60 border border-slate-800 rounded-xl p-3">
            <div className="text-[10px] uppercase text-slate-500 font-bold mb-1">Total Runs</div>
            <div className="text-lg font-bold text-white">{metrics.runs || 0}</div>
          </div>
          <div className="bg-slate-900/60 border border-slate-800 rounded-xl p-3">
            <div className="text-[10px] uppercase text-slate-500 font-bold mb-1">Commits / Overrides</div>
            <div className="text-lg font-bold text-white">
              <span className="text-emerald-400">{metrics.commits || 0}</span>
              <span className="text-slate-500 mx-1">/</span>
              <span className="text-amber-400">{metrics.overrides || 0}</span>
            </div>
          </div>
          <div className="bg-slate-900/60 border border-slate-800 rounded-xl p-3">
            <div className="text-[10px] uppercase text-slate-500 font-bold mb-1">Tool Executions</div>
            <div className="text-lg font-bold text-violet-400">{metrics.toolsExecuted || 0}</div>
          </div>
          <div className="bg-slate-900/60 border border-slate-800 rounded-xl p-3">
            <div className="text-[10px] uppercase text-slate-500 font-bold mb-1">Tokens (7d)</div>
            <div className="text-lg font-bold text-sky-400">
              {((metrics.tokens?.inputTokens || 0) + (metrics.tokens?.outputTokens || 0)).toLocaleString()}
            </div>
          </div>
        </div>
      )}

      {/* History List */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden">
        <div className="px-4 py-3 border-b border-slate-800 flex items-center justify-between">
          <span className="text-xs font-bold text-slate-300 flex items-center gap-1.5">
            <Clock size={14} className="text-slate-400" /> Recent Agent Executions
          </span>
          <span className="text-[10px] text-slate-500 font-mono">Last {history.length} runs</span>
        </div>
        
        {history.length === 0 && !loading ? (
          <div className="p-8 text-center text-sm text-slate-500">
            No agent execution history found for this tenant.
          </div>
        ) : (
          <div className="divide-y divide-slate-800/60 max-h-[600px] overflow-y-auto">
            {history.map((run: any) => (
              <div key={run.id || run.Id} className="p-4 hover:bg-slate-800/30 transition-colors flex flex-col gap-2">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    {run.success || run.Success ? (
                      <CheckCircle size={14} className="text-emerald-400" />
                    ) : (
                      <XCircle size={14} className="text-rose-400" />
                    )}
                    <span className="text-sm font-bold text-slate-200">
                      {run.agentAlias || run.AgentAlias || 'Unknown Agent'}
                    </span>
                    <span className="text-[10px] font-mono px-2 py-0.5 rounded bg-slate-800 text-slate-400 border border-slate-700">
                      {run.providerAlias || run.ProviderAlias || 'default'}
                    </span>
                  </div>
                  <span className="text-[10px] text-slate-500">
                    {new Date(run.createdAtUtc || run.CreatedAtUtc).toLocaleString()}
                  </span>
                </div>
                
                <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-xs mt-1">
                  <div className="flex items-center gap-1.5 text-slate-400">
                    <FileText size={12} />
                    <span>Workflow Instance:</span>
                    <span className="text-slate-300 font-mono truncate">{run.workflowInstanceId || run.WorkflowInstanceId || 'N/A'}</span>
                  </div>
                  
                  <div className="flex items-center gap-1.5 text-slate-400">
                    <Activity size={12} />
                    <span>Decision:</span>
                    <span className={`font-bold ${
                      (run.decisionAction || run.DecisionAction) === 'Commit' ? 'text-emerald-400' :
                      (run.decisionAction || run.DecisionAction) === 'NeedsReview' ? 'text-amber-400' :
                      'text-rose-400'
                    }`}>
                      {run.decisionAction || run.DecisionAction || 'Unknown'}
                    </span>
                  </div>
                  
                  {(run.toolsUsed || run.ToolsUsed) > 0 && (
                    <div className="flex items-center gap-1.5 text-slate-400">
                      <Wrench size={12} className="text-violet-400" />
                      <span>Tools Used:</span>
                      <span className="text-violet-300 font-bold">{run.toolsUsed || run.ToolsUsed}</span>
                    </div>
                  )}

                  {(run.totalTokens || run.TotalTokens) > 0 && (
                    <div className="flex items-center gap-1.5 text-slate-400">
                      <Cpu size={12} className="text-sky-400" />
                      <span>Tokens:</span>
                      <span className="text-sky-300 font-mono">{(run.totalTokens || run.TotalTokens).toLocaleString()}</span>
                    </div>
                  )}
                </div>
                
                {(run.rationale || run.Rationale) && (
                  <div className="mt-2 text-[11px] text-slate-400 bg-slate-900/80 p-2.5 rounded-lg border border-slate-800/60 leading-relaxed italic border-l-2 border-l-purple-500/50">
                    <div className="flex items-center gap-1 mb-1 text-purple-400/80 font-semibold uppercase tracking-wider text-[9px]">
                      <Bot size={10} /> Model Rationale
                    </div>
                    {run.rationale || run.Rationale}
                  </div>
                )}
                
                {!(run.success || run.Success) && (run.errorMessage || run.ErrorMessage) && (
                  <div className="mt-2 text-[11px] text-rose-300 bg-rose-950/30 p-2.5 rounded-lg border border-rose-900/50 flex items-start gap-2">
                    <AlertTriangle size={14} className="shrink-0 mt-0.5" />
                    <span className="break-words">{run.errorMessage || run.ErrorMessage}</span>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};
