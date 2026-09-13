import React, { useState, useEffect } from 'react';
import { DeadLetterDto, AuthSession } from '../types';
import { api } from '../api/client';
import { 
  AlertTriangle, RefreshCw, RotateCcw, Trash2, Eye, 
  CheckCircle2, Globe, Bell, Send, Clock, X, Terminal, Filter
} from 'lucide-react';

interface Props {
  session: AuthSession;
  tenantFilter?: string;
}

export const DeadLetterQueueViewer: React.FC<Props> = ({ session, tenantFilter }) => {
  const [deadLetters, setDeadLetters] = useState<DeadLetterDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [actionSuccess, setActionSuccess] = useState<string | null>(null);
  const [typeFilter, setTypeFilter] = useState<string>('');
  const [selectedLetter, setSelectedLetter] = useState<DeadLetterDto | null>(null);
  const [isRetryingAll, setIsRetryingAll] = useState(false);
  const [processingId, setProcessingId] = useState<string | null>(null);

  const fetchDeadLetters = async () => {
    setLoading(true);
    setError(null);
    try {
      const items = await api.listDeadLetters(
        tenantFilter || (session.role === 'Tenant' ? session.tenantId : undefined),
        typeFilter || undefined,
        session.role
      );
      setDeadLetters(items);
    } catch (err: any) {
      setError(err.message || 'Failed to load dead letters');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchDeadLetters();
  }, [tenantFilter, typeFilter]);

  const handleRetrySingle = async (id: string) => {
    setProcessingId(id);
    setError(null);
    setActionSuccess(null);
    try {
      await api.retryDeadLetter(id, session.role);
      setActionSuccess(`Message ${id.substring(0, 8)}... replayed for re-dispatch.`);
      await fetchDeadLetters();
    } catch (err: any) {
      setError(err.message || 'Retry failed');
    } finally {
      setProcessingId(null);
    }
  };

  const handleRetryAll = async () => {
    if (!window.confirm('Are you sure you want to replay all dead letter messages?')) return;
    setIsRetryingAll(true);
    setError(null);
    setActionSuccess(null);
    try {
      const result = await api.retryAllDeadLetters(typeFilter || undefined, session.role);
      setActionSuccess(`Successfully replayed ${result.replayedCount} dead letter message(s).`);
      await fetchDeadLetters();
    } catch (err: any) {
      setError(err.message || 'Failed to replay all messages');
    } finally {
      setIsRetryingAll(false);
    }
  };

  const handlePurgeSingle = async (id: string) => {
    if (!window.confirm('Permanently purge this dead letter message? This cannot be undone.')) return;
    setProcessingId(id);
    setError(null);
    setActionSuccess(null);
    try {
      await api.purgeDeadLetter(id, session.role);
      setActionSuccess(`Message ${id.substring(0, 8)}... purged.`);
      if (selectedLetter?.id === id) setSelectedLetter(null);
      await fetchDeadLetters();
    } catch (err: any) {
      setError(err.message || 'Purge failed');
    } finally {
      setProcessingId(null);
    }
  };

  const formatPayload = (payload: string) => {
    try {
      return JSON.stringify(JSON.parse(payload), null, 2);
    } catch {
      return payload;
    }
  };

  return (
    <div className="space-y-6">
      {/* Header & Controls */}
      <div className="bg-slate-800/80 border border-slate-700/80 rounded-2xl p-6 shadow-xl backdrop-blur-md">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="p-3 bg-red-500/10 border border-red-500/30 rounded-xl text-red-400">
              <AlertTriangle className="w-6 h-6" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-xl font-bold text-white tracking-tight">Dead Letter Queue (DLQ)</h2>
                <span className="px-2.5 py-0.5 text-xs font-semibold rounded-full bg-red-500/20 text-red-300 border border-red-500/30">
                  {deadLetters.length} Failed
                </span>
              </div>
              <p className="text-xs text-slate-400 mt-1">
                Outbox messages that exhausted retry limits with exponential backoff. Inspect failures, review payloads, and trigger replays.
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2 flex-wrap">
            <div className="relative">
              <Filter className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
              <input
                type="text"
                value={typeFilter}
                onChange={(e) => setTypeFilter(e.target.value)}
                placeholder="Filter by type..."
                className="pl-8 pr-3 py-1.5 bg-slate-900 border border-slate-700 rounded-xl text-xs text-slate-200 placeholder-slate-500 focus:outline-none focus:border-blue-500 w-40"
              />
            </div>

            <button
              onClick={fetchDeadLetters}
              disabled={loading}
              className="p-2 text-slate-300 hover:text-white bg-slate-700/50 hover:bg-slate-700 border border-slate-600 rounded-xl transition-all disabled:opacity-50"
              title="Refresh Queue"
            >
              <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
            </button>

            <button
              onClick={handleRetryAll}
              disabled={isRetryingAll || deadLetters.length === 0}
              className="px-3.5 py-1.5 text-xs font-medium text-amber-300 bg-amber-500/10 hover:bg-amber-500/20 border border-amber-500/30 rounded-xl transition-all flex items-center gap-1.5 disabled:opacity-40"
            >
              <RotateCcw className={`w-3.5 h-3.5 ${isRetryingAll ? 'animate-spin' : ''}`} />
              Replay All
            </button>
          </div>
        </div>

        {/* Notifications & Feedback */}
        {actionSuccess && (
          <div className="mt-4 p-3 bg-emerald-500/10 border border-emerald-500/30 rounded-xl text-xs text-emerald-300 flex items-center justify-between">
            <span>{actionSuccess}</span>
            <button onClick={() => setActionSuccess(null)} className="text-emerald-400 hover:text-emerald-200">
              <X className="w-3.5 h-3.5" />
            </button>
          </div>
        )}

        {error && (
          <div className="mt-4 p-3 bg-red-500/10 border border-red-500/30 rounded-xl text-xs text-red-300 flex items-center justify-between">
            <span>{error}</span>
            <button onClick={() => setError(null)} className="text-red-400 hover:text-red-200">
              <X className="w-3.5 h-3.5" />
            </button>
          </div>
        )}
      </div>

      {/* Dead Letter Table or Empty State */}
      {deadLetters.length === 0 && !loading ? (
        <div className="bg-slate-800/40 border border-slate-700/60 rounded-2xl p-12 text-center">
          <div className="w-12 h-12 bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 rounded-full flex items-center justify-center mx-auto mb-3">
            <CheckCircle2 className="w-6 h-6" />
          </div>
          <h3 className="text-base font-semibold text-white">Dead Letter Queue is Clean</h3>
          <p className="text-xs text-slate-400 mt-1 max-w-md mx-auto">
            All lifecycle actions, outbox webhooks, notifications, and domain events have dispatched successfully with zero persistent failures.
          </p>
        </div>
      ) : (
        <div className="bg-slate-800/80 border border-slate-700/80 rounded-2xl shadow-xl overflow-hidden backdrop-blur-md">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-300">
              <thead className="bg-slate-900/60 text-slate-400 uppercase text-[10px] tracking-wider border-b border-slate-700/80">
                <tr>
                  <th className="py-3 px-4">Status & Type</th>
                  <th className="py-3 px-4">Target / Endpoint</th>
                  <th className="py-3 px-4">Step</th>
                  <th className="py-3 px-4">Retries</th>
                  <th className="py-3 px-4">Failure Reason</th>
                  <th className="py-3 px-4">Occurred</th>
                  <th className="py-3 px-4 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-700/50">
                {deadLetters.map((letter) => {
                  const isProcessing = processingId === letter.id;
                  return (
                    <tr key={letter.id} className="hover:bg-slate-700/20 transition-colors">
                      <td className="py-3 px-4">
                        <div className="flex items-center gap-2">
                          <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-red-500/20 text-red-300 border border-red-500/30">
                            DLQ
                          </span>
                          <span className="font-mono text-slate-200 font-medium">
                            {letter.actionType || letter.type.replace('WorkflowAction:', '')}
                          </span>
                        </div>
                      </td>
                      <td className="py-3 px-4">
                        {letter.targetUrl ? (
                          <div className="flex items-center gap-1.5 font-mono text-[11px] text-blue-300 max-w-xs truncate" title={letter.targetUrl}>
                            <Globe className="w-3 h-3 text-blue-400 shrink-0" />
                            <span className="text-slate-400 font-semibold">{letter.httpMethod || 'POST'}</span>
                            <span className="truncate">{letter.targetUrl}</span>
                          </div>
                        ) : letter.actionType === 'Notification' ? (
                          <div className="flex items-center gap-1.5 text-purple-300">
                            <Bell className="w-3 h-3 text-purple-400 shrink-0" />
                            <span>In-App Alert</span>
                          </div>
                        ) : (
                          <div className="flex items-center gap-1.5 text-amber-300">
                            <Send className="w-3 h-3 text-amber-400 shrink-0" />
                            <span>{letter.type}</span>
                          </div>
                        )}
                      </td>
                      <td className="py-3 px-4">
                        {letter.stepId ? (
                          <span className="px-1.5 py-0.5 rounded bg-slate-700/60 font-mono text-[11px] text-slate-300">
                            {letter.stepId}
                          </span>
                        ) : (
                          <span className="text-slate-500">-</span>
                        )}
                      </td>
                      <td className="py-3 px-4">
                        <span className="font-mono text-red-400 font-semibold">
                          {letter.retryCount} / {letter.maxRetries}
                        </span>
                      </td>
                      <td className="py-3 px-4 max-w-xs">
                        <div className="text-red-300/90 truncate font-mono text-[11px]" title={letter.error || 'Unknown failure'}>
                          {letter.error || 'Unknown failure'}
                        </div>
                      </td>
                      <td className="py-3 px-4 text-slate-400 whitespace-nowrap">
                        <div className="flex items-center gap-1">
                          <Clock className="w-3 h-3 text-slate-500" />
                          <span>{new Date(letter.occurredOnUtc).toLocaleTimeString()}</span>
                        </div>
                      </td>
                      <td className="py-3 px-4 text-right">
                        <div className="flex items-center justify-end gap-1.5">
                          <button
                            onClick={() => setSelectedLetter(letter)}
                            className="p-1.5 text-slate-400 hover:text-white bg-slate-700/40 hover:bg-slate-700 rounded-lg transition-all"
                            title="Inspect Payload & Error"
                          >
                            <Eye className="w-3.5 h-3.5" />
                          </button>
                          <button
                            onClick={() => handleRetrySingle(letter.id)}
                            disabled={isProcessing}
                            className="px-2.5 py-1 text-xs font-medium text-blue-300 bg-blue-500/10 hover:bg-blue-500/20 border border-blue-500/30 rounded-lg transition-all flex items-center gap-1 disabled:opacity-50"
                            title="Replay from Dead Letter"
                          >
                            <RotateCcw className={`w-3 h-3 ${isProcessing ? 'animate-spin' : ''}`} />
                            <span>Retry</span>
                          </button>
                          <button
                            onClick={() => handlePurgeSingle(letter.id)}
                            disabled={isProcessing}
                            className="p-1.5 text-red-400 hover:text-red-300 bg-red-500/10 hover:bg-red-500/20 border border-red-500/30 rounded-lg transition-all"
                            title="Purge Message"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Detail & Diagnostic Modal */}
      {selectedLetter && (
        <div className="fixed inset-0 z-50 bg-black/70 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="bg-slate-800 border border-slate-700 rounded-2xl w-full max-w-2xl shadow-2xl overflow-hidden flex flex-col max-h-[85vh]">
            <div className="p-4 border-b border-slate-700 flex items-center justify-between bg-slate-850">
              <div className="flex items-center gap-2">
                <Terminal className="w-4 h-4 text-red-400" />
                <h3 className="font-bold text-white text-sm">Dead Letter Diagnostics</h3>
                <span className="font-mono text-xs text-slate-400">({selectedLetter.id})</span>
              </div>
              <button
                onClick={() => setSelectedLetter(null)}
                className="text-slate-400 hover:text-white p-1 rounded-lg hover:bg-slate-700"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <div className="p-6 space-y-4 overflow-y-auto flex-1">
              {/* Error Box */}
              <div>
                <label className="text-xs font-bold text-red-400 uppercase tracking-wider block mb-1">
                  Failure Stack / Reason
                </label>
                <div className="bg-red-950/40 border border-red-500/30 rounded-xl p-3 text-red-200 font-mono text-xs whitespace-pre-wrap break-all">
                  {selectedLetter.error || 'No error details recorded.'}
                </div>
              </div>

              {/* Metadata Grid */}
              <div className="grid grid-cols-2 gap-3 text-xs bg-slate-900/60 p-3 rounded-xl border border-slate-700/60">
                <div>
                  <span className="text-slate-500 block">Message Type:</span>
                  <span className="font-mono text-slate-200 font-semibold">{selectedLetter.type}</span>
                </div>
                <div>
                  <span className="text-slate-500 block">Retry Count:</span>
                  <span className="font-mono text-red-400 font-semibold">
                    {selectedLetter.retryCount} of {selectedLetter.maxRetries} max
                  </span>
                </div>
                <div>
                  <span className="text-slate-500 block">Occurred At:</span>
                  <span className="font-mono text-slate-200">
                    {new Date(selectedLetter.occurredOnUtc).toLocaleString()}
                  </span>
                </div>
                <div>
                  <span className="text-slate-500 block">Tenant ID:</span>
                  <span className="font-mono text-slate-300">{selectedLetter.tenantId}</span>
                </div>
              </div>

              {/* Payload Box */}
              <div>
                <label className="text-xs font-bold text-slate-400 uppercase tracking-wider block mb-1">
                  Enqueued Payload (JSON)
                </label>
                <pre className="bg-slate-950 border border-slate-800 rounded-xl p-3 font-mono text-xs text-emerald-400 overflow-x-auto max-h-60">
                  {formatPayload(selectedLetter.payload)}
                </pre>
              </div>
            </div>

            <div className="p-4 border-t border-slate-700 bg-slate-850 flex items-center justify-between">
              <button
                onClick={() => handlePurgeSingle(selectedLetter.id)}
                className="px-3 py-1.5 text-xs font-medium text-red-400 hover:text-red-300 bg-red-500/10 hover:bg-red-500/20 border border-red-500/30 rounded-xl transition-all flex items-center gap-1.5"
              >
                <Trash2 className="w-3.5 h-3.5" />
                Purge
              </button>

              <div className="flex items-center gap-2">
                <button
                  onClick={() => setSelectedLetter(null)}
                  className="px-3 py-1.5 text-xs text-slate-300 hover:text-white bg-slate-700 rounded-xl"
                >
                  Close
                </button>
                <button
                  onClick={() => handleRetrySingle(selectedLetter.id)}
                  className="px-3.5 py-1.5 text-xs font-medium text-white bg-blue-600 hover:bg-blue-500 rounded-xl transition-all flex items-center gap-1.5"
                >
                  <RotateCcw className="w-3.5 h-3.5" />
                  Replay Now
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
