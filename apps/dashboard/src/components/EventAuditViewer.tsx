import React, { useState, useEffect } from 'react';
import { PublishedEventDto } from '../types';
import { api } from '../api/client';
import { 
  Activity, Search, RefreshCw, Copy, Check, Clock, 
  Sparkles, ShieldAlert, CheckCircle, FileJson, X, ExternalLink,
  ArrowRight, Hash, Eye
} from 'lucide-react';

interface Props {
  role: 'Tenant' | 'Admin';
  onInspectWorkflow?: (instanceId: string) => void;
}

export const EventAuditViewer: React.FC<Props> = ({ role, onInspectWorkflow }) => {
  const [events, setEvents] = useState<PublishedEventDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copiedId, setCopiedId] = useState<string | null>(null);

  // Filters
  const [selectedInstanceFilter, setSelectedInstanceFilter] = useState<string>('');
  const [eventTypeFilter, setEventTypeFilter] = useState<string>('All');
  const [searchQuery, setSearchQuery] = useState<string>('');

  // Selected event for deep investigation modal
  const [selectedEvent, setSelectedEvent] = useState<PublishedEventDto | null>(null);

  const fetchEvents = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.listEvents(selectedInstanceFilter || undefined, 100, role);
      setEvents(data);
    } catch (err: any) {
      setError(err.message || 'Failed to load event audit stream');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchEvents();
  }, [selectedInstanceFilter, role]);

  const handleCopy = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  // Filtered events
  const filteredEvents = events.filter(e => {
    if (eventTypeFilter !== 'All') {
      if (eventTypeFilter === 'Transitions' && !e.eventType.startsWith('EVT-')) return false;
      if (eventTypeFilter === 'Insights' && !e.eventType.includes('Insight')) return false;
      if (eventTypeFilter === 'Lifecycle' && !e.eventType.includes('Workflow')) return false;
      if (eventTypeFilter === 'Tasks' && !e.eventType.includes('Task')) return false;
    }

    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase();
      const inType = e.eventType.toLowerCase().includes(q);
      const inCorrelation = e.correlationId?.toLowerCase().includes(q) || false;
      const inPayload = e.payloadJson?.toLowerCase().includes(q) || false;
      const inMetadata = Object.values(e.metadata || {}).some(v => v.toLowerCase().includes(q));
      return inType || inCorrelation || inPayload || inMetadata;
    }

    return true;
  });

  const getEventBadge = (type: string) => {
    if (type.includes('Insight')) {
      return (
        <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-purple-500/20 text-purple-300 border border-purple-500/30 inline-flex items-center gap-1">
          <Sparkles size={11} /> AI Insight
        </span>
      );
    }
    if (type.includes('Escalat') || type.includes('Timeout')) {
      return (
        <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-rose-500/20 text-rose-300 border border-rose-500/30 inline-flex items-center gap-1">
          <ShieldAlert size={11} /> SLA Breach
        </span>
      );
    }
    if (type === 'WorkflowStarted') {
      return (
        <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-cyan-500/20 text-cyan-300 border border-cyan-500/30 inline-flex items-center gap-1">
          <Activity size={11} /> Workflow Inception
        </span>
      );
    }
    if (type === 'WorkflowCompleted') {
      return (
        <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 inline-flex items-center gap-1">
          <CheckCircle size={11} /> Completed
        </span>
      );
    }
    if (type.includes('Task')) {
      return (
        <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-amber-500/20 text-amber-300 border border-amber-500/30 inline-flex items-center gap-1">
          <CheckCircle size={11} /> Task Completed
        </span>
      );
    }
    return (
      <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-blue-500/20 text-blue-300 border border-blue-500/30 inline-flex items-center gap-1 font-mono">
        {type}
      </span>
    );
  };

  return (
    <div className="space-y-4">
      {/* Search & Filter Header Bar */}
      <div className="bg-slate-850 p-4 rounded-xl border border-slate-700/80 flex flex-col md:flex-row items-center justify-between gap-3">
        <div className="flex items-center gap-2 w-full md:w-auto">
          <div className="relative flex-1 md:w-72">
            <Search className="absolute left-3 top-2.5 text-slate-500" size={14} />
            <input
              type="text"
              placeholder="Search event type, UUID, payload..."
              value={searchQuery}
              onChange={e => setSearchQuery(e.target.value)}
              className="w-full bg-slate-900 border border-slate-700 rounded-lg pl-9 pr-3 py-1.5 text-xs text-slate-200 focus:outline-none focus:border-blue-500"
            />
            {searchQuery && (
              <button onClick={() => setSearchQuery('')} className="absolute right-2.5 top-2 text-slate-500 hover:text-slate-300">
                <X size={13} />
              </button>
            )}
          </div>

          {selectedInstanceFilter && (
            <div className="flex items-center gap-1 px-2.5 py-1 bg-blue-500/20 border border-blue-500/30 rounded-lg text-xs text-blue-300">
              <span>Instance: <strong>{selectedInstanceFilter.substring(0, 8)}...</strong></span>
              <button onClick={() => setSelectedInstanceFilter('')} className="text-blue-300 hover:text-white ml-1">
                <X size={12} />
              </button>
            </div>
          )}
        </div>

        {/* Category Pills & Refresh */}
        <div className="flex items-center gap-2 w-full md:w-auto justify-between md:justify-end overflow-x-auto pb-1 md:pb-0">
          <div className="flex bg-slate-900 rounded-lg p-1 border border-slate-700 text-xs">
            {(['All', 'Transitions', 'Lifecycle', 'Tasks', 'Insights'] as const).map(cat => (
              <button
                key={cat}
                onClick={() => setEventTypeFilter(cat)}
                className={`px-2.5 py-1 rounded-md text-[11px] font-medium transition-all ${
                  eventTypeFilter === cat ? 'bg-blue-600 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
                }`}
              >
                {cat}
              </button>
            ))}
          </div>

          <button
            onClick={fetchEvents}
            disabled={loading}
            className="p-1.5 bg-slate-800 hover:bg-slate-750 text-slate-300 border border-slate-700 rounded-lg transition-colors"
            title="Refresh event stream"
          >
            <RefreshCw size={14} className={loading ? 'animate-spin text-blue-400' : ''} />
          </button>
        </div>
      </div>

      {error && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/30 rounded-xl text-xs text-rose-300">
          {error}
        </div>
      )}

      {/* Events Table */}
      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs text-slate-300">
          <thead className="bg-slate-800/70 text-[11px] text-slate-400 uppercase tracking-wider">
            <tr>
              <th className="py-3 px-4 rounded-l-lg">Timestamp (UTC)</th>
              <th className="py-3 px-4">Event Type</th>
              <th className="py-3 px-4">Workflow Instance</th>
              <th className="py-3 px-4">Transition Context</th>
              <th className="py-3 px-4">Payload Status</th>
              <th className="py-3 px-4 text-right rounded-r-lg">Investigation</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800">
            {filteredEvents.map((evt) => {
              const meta = evt.metadata || {};
              const fromState = meta.FromState;
              const toState = meta.ToState;
              const fromStep = meta.FromStep;
              const toStep = meta.ToStep;
              const hasPayload = Boolean(evt.payloadJson && evt.payloadJson !== 'null' && evt.payloadJson !== '{}');

              return (
                <tr key={evt.eventId} className="hover:bg-slate-800/40 transition-colors">
                  <td className="py-3 px-4 whitespace-nowrap text-slate-400 font-mono text-[11px]">
                    <div className="flex items-center gap-1.5">
                      <Clock size={12} className="text-slate-500" />
                      <span>{new Date(evt.timestamp).toLocaleTimeString()}</span>
                      <span className="text-[10px] text-slate-600">{new Date(evt.timestamp).toLocaleDateString()}</span>
                    </div>
                  </td>
                  <td className="py-3 px-4 whitespace-nowrap">
                    {getEventBadge(evt.eventType)}
                  </td>
                  <td className="py-3 px-4 whitespace-nowrap font-mono text-slate-400">
                    {evt.correlationId ? (
                      <div className="flex items-center gap-1.5">
                        <button
                          onClick={() => setSelectedInstanceFilter(evt.correlationId!)}
                          className="text-white hover:text-blue-400 hover:underline font-medium"
                          title="Click to filter by this workflow instance"
                        >
                          {evt.correlationId.substring(0, 8)}...
                        </button>
                        <button
                          onClick={() => handleCopy(evt.correlationId!, evt.eventId + '-corr')}
                          className="p-1 hover:text-white text-slate-500 rounded"
                          title="Copy UUID"
                        >
                          {copiedId === evt.eventId + '-corr' ? <Check size={11} className="text-emerald-400" /> : <Copy size={11} />}
                        </button>
                      </div>
                    ) : (
                      <span className="text-slate-600 italic">Global</span>
                    )}
                  </td>
                  <td className="py-3 px-4">
                    {fromState && toState ? (
                      <div className="flex items-center gap-1 text-[11px]">
                        <span className="text-slate-400 font-mono">{fromState}</span>
                        <ArrowRight size={11} className="text-blue-400" />
                        <span className="text-emerald-300 font-mono font-bold">{toState}</span>
                        {fromStep && toStep && fromStep !== fromState && (
                          <span className="text-slate-500 text-[10px] ml-1">({fromStep}→{toStep})</span>
                        )}
                      </div>
                    ) : meta.WorkflowName ? (
                      <span className="text-slate-400 text-[11px]">Started: <strong className="text-white">{meta.WorkflowName}</strong></span>
                    ) : meta.Agent ? (
                      <span className="text-purple-300 text-[11px]">Advisory: <strong>{meta.Agent}</strong></span>
                    ) : (
                      <span className="text-slate-500 text-[11px]">Standard telemetry record</span>
                    )}
                  </td>
                  <td className="py-3 px-4">
                    {hasPayload ? (
                      <span className="px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 text-[10px] font-mono inline-flex items-center gap-1">
                        <FileJson size={11} /> JSON Attached
                      </span>
                    ) : (
                      <span className="text-slate-600 text-[10px]">None</span>
                    )}
                  </td>
                  <td className="py-3 px-4 text-right whitespace-nowrap space-x-2">
                    <button
                      onClick={() => setSelectedEvent(evt)}
                      className="px-2.5 py-1 bg-slate-800 hover:bg-slate-700 text-slate-200 border border-slate-700 rounded text-xs font-semibold inline-flex items-center gap-1 transition-colors"
                    >
                      <Eye size={12} /> Inspect
                    </button>
                  </td>
                </tr>
              );
            })}

            {filteredEvents.length === 0 && !loading && (
              <tr>
                <td colSpan={6} className="py-16 text-center text-slate-400">
                  <Activity size={32} className="mx-auto mb-2 text-slate-600 opacity-60" />
                  <div className="font-semibold text-slate-300">No events matched your filter.</div>
                  <p className="text-xs text-slate-500 mt-1">
                    Publish events via the REST API, MCP server, or the Process Simulator above.
                  </p>
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Deep Event & Payload Investigation Modal */}
      {selectedEvent && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-2xl w-full p-6 shadow-2xl space-y-4 max-h-[90vh] flex flex-col">
            
            {/* Header */}
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <div className="flex items-center gap-2">
                <FileJson className="text-blue-400" size={22} />
                <div>
                  <h3 className="text-base font-bold text-white flex items-center gap-2">
                    Event Investigation
                    {getEventBadge(selectedEvent.eventType)}
                  </h3>
                  <p className="text-[11px] text-slate-400 font-mono mt-0.5">
                    Event ID: {selectedEvent.eventId}
                  </p>
                </div>
              </div>
              <button
                onClick={() => setSelectedEvent(null)}
                className="text-slate-400 hover:text-white p-1 rounded transition-colors"
              >
                <X size={20} />
              </button>
            </div>

            {/* Event Facts Card */}
            <div className="grid grid-cols-2 gap-3 bg-slate-800/60 border border-slate-700/60 p-3.5 rounded-xl text-xs">
              <div>
                <span className="text-slate-400 text-[11px] block">Workflow Instance UUID</span>
                <span className="font-mono text-white font-medium break-all">
                  {selectedEvent.correlationId || 'None'}
                </span>
              </div>
              <div>
                <span className="text-slate-400 text-[11px] block">Recorded Timestamp</span>
                <span className="text-slate-200">
                  {new Date(selectedEvent.timestamp).toUTCString()}
                </span>
              </div>
              <div>
                <span className="text-slate-400 text-[11px] block">Tenant Isolation</span>
                <span className="font-mono text-slate-300">
                  {selectedEvent.tenantId}
                </span>
              </div>
              <div>
                <span className="text-slate-400 text-[11px] block">Actor / Origin</span>
                <span className="text-slate-200 font-mono">
                  {selectedEvent.metadata?.ActorId || 'System Kernel'}
                </span>
              </div>
            </div>

            {/* Transitions Metadata */}
            {selectedEvent.metadata && Object.keys(selectedEvent.metadata).length > 0 && (
              <div className="space-y-1.5">
                <div className="text-xs font-semibold text-slate-300 flex items-center gap-1.5">
                  <Hash size={13} className="text-blue-400" /> Event Metadata & State Transitions
                </div>
                <div className="grid grid-cols-2 gap-2 max-h-36 overflow-y-auto">
                  {Object.entries(selectedEvent.metadata)
                    .filter(([k]) => k !== 'Payload')
                    .map(([k, v]) => (
                      <div key={k} className="p-2 bg-slate-950/60 border border-slate-800 rounded-lg text-xs flex justify-between items-center">
                        <span className="text-slate-400 font-mono text-[11px]">{k}:</span>
                        <span className="text-slate-200 font-medium font-mono text-[11px]">{v}</span>
                      </div>
                    ))}
                </div>
              </div>
            )}

            {/* JSON Payload Viewer */}
            <div className="flex-1 overflow-hidden flex flex-col space-y-1.5">
              <div className="flex items-center justify-between text-xs">
                <span className="font-semibold text-slate-300 flex items-center gap-1.5">
                  <FileJson size={13} className="text-emerald-400" /> Attached JSON Payload
                </span>
                {selectedEvent.payloadJson && (
                  <button
                    onClick={() => handleCopy(selectedEvent.payloadJson!, 'payload')}
                    className="text-xs text-blue-400 hover:text-white flex items-center gap-1 font-mono"
                  >
                    {copiedId === 'payload' ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
                    Copy Payload
                  </button>
                )}
              </div>

              <div className="flex-1 overflow-y-auto bg-slate-950 border border-slate-800 rounded-xl p-3 font-mono text-xs text-emerald-300/90 max-h-56 leading-relaxed">
                {selectedEvent.payloadJson ? (
                  <pre>{(() => {
                    try {
                      return JSON.stringify(JSON.parse(selectedEvent.payloadJson), null, 2);
                    } catch {
                      return selectedEvent.payloadJson;
                    }
                  })()}</pre>
                ) : (
                  <span className="text-slate-500 italic">No custom JSON payload was published with this event.</span>
                )}
              </div>
            </div>

            {/* Actions Footer */}
            <div className="pt-3 border-t border-slate-800 flex items-center justify-between">
              {selectedEvent.correlationId && onInspectWorkflow ? (
                <button
                  onClick={() => {
                    const instId = selectedEvent.correlationId!;
                    setSelectedEvent(null);
                    onInspectWorkflow(instId);
                  }}
                  className="px-3.5 py-1.5 bg-blue-600/20 hover:bg-blue-600/30 border border-blue-500/30 text-blue-300 rounded-lg text-xs font-semibold inline-flex items-center gap-1.5 transition-all"
                >
                  <ExternalLink size={13} /> View Complete Workflow Audit
                </button>
              ) : <div />}

              <button
                onClick={() => setSelectedEvent(null)}
                className="px-4 py-1.5 bg-slate-800 hover:bg-slate-700 text-white rounded-lg text-xs font-semibold transition-colors"
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
