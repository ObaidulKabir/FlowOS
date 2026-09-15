import React, { useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  ChevronRight,
  Clipboard,
  FlaskConical,
  Play,
  Plus,
  RefreshCw,
  RotateCcw,
  ShieldCheck,
  Trash2,
  Zap
} from 'lucide-react';
import { api } from '../api/client';
import {
  WorkflowContextBinding,
  WorkflowContextBindingRevision,
  WorkflowContextSimulationEventRequest,
  WorkflowContextSimulationResult
} from '../types';
import { WorkflowGraphVisualizer } from './WorkflowGraphVisualizer';

interface Props {
  role?: 'Tenant' | 'Admin';
  initialBindingId?: string;
  initialRevision?: 'draft' | 'active';
}

interface EditableEvent {
  eventType: string;
  payloadJson: string;
  roles: string;
}

const emptyObjectJson = '{\n  \n}';

const parseSchema = (schema?: string): any | undefined => {
  if (!schema) return undefined;
  try {
    return JSON.parse(schema);
  } catch {
    return undefined;
  }
};

const seedValue = (schema: any): unknown => {
  if (!schema) return {};
  if (schema.default !== undefined) return schema.default;
  if (schema.type === 'object' || schema.properties) {
    return Object.fromEntries(
      Object.entries(schema.properties || {}).map(([key, child]) => [key, seedValue(child)])
    );
  }
  if (schema.type === 'array') return [];
  if (schema.type === 'number' || schema.type === 'integer') return 0;
  if (schema.type === 'boolean') return false;
  return '';
};

const seedJson = (schema?: string) => JSON.stringify(seedValue(parseSchema(schema)), null, 2);

const parseObject = (value: string, label: string): Record<string, unknown> => {
  const parsed = JSON.parse(value || '{}');
  if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object') {
    throw new Error(`${label} must be a JSON object.`);
  }
  return parsed;
};

const formatJson = (value: unknown) => JSON.stringify(value ?? {}, null, 2);

export const ContextSimulationStudio: React.FC<Props> = ({
  role = 'Tenant',
  initialBindingId,
  initialRevision
}) => {
  const [bindings, setBindings] = useState<WorkflowContextBinding[]>([]);
  const [bindingId, setBindingId] = useState(initialBindingId || '');
  const [revision, setRevision] = useState<'draft' | 'active'>(initialRevision || 'draft');
  const [initialPayloadJson, setInitialPayloadJson] = useState(emptyObjectJson);
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  const [events, setEvents] = useState<EditableEvent[]>([]);
  const [maxSteps, setMaxSteps] = useState(25);
  const [result, setResult] = useState<WorkflowContextSimulationResult>();
  const [selectedTraceIndex, setSelectedTraceIndex] = useState(0);
  const [busy, setBusy] = useState(false);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string>();
  const [copied, setCopied] = useState(false);

  const selectedBinding = useMemo(
    () => bindings.find(binding => binding.id === bindingId),
    [bindings, bindingId]
  );
  const selectedRevision: WorkflowContextBindingRevision | undefined =
    revision === 'draft'
      ? selectedBinding?.draftRevision
      : selectedBinding?.activeRevision;

  const loadBindings = async () => {
    setBusy(true);
    setError(undefined);
    try {
      const loaded = await api.listContextBindings(undefined, role);
      setBindings(loaded);
      setBindingId(current => {
        if (initialBindingId && loaded.some(item => item.id === initialBindingId)) return initialBindingId;
        if (loaded.some(item => item.id === current)) return current;
        return loaded[0]?.id || '';
      });
    } catch (err: any) {
      setError(err.message || 'Failed to load context bindings.');
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    loadBindings();
  }, []);

  useEffect(() => {
    if (!initialBindingId) return;
    setBindingId(initialBindingId);
    if (initialRevision) setRevision(initialRevision);
  }, [initialBindingId, initialRevision]);

  useEffect(() => {
    if (!selectedBinding) return;
    if (revision === 'draft' && !selectedBinding.draftRevision && selectedBinding.activeRevision) {
      setRevision('active');
      return;
    }
    if (revision === 'active' && !selectedBinding.activeRevision && selectedBinding.draftRevision) {
      setRevision('draft');
      return;
    }
    if (!selectedRevision) return;
    setInitialPayloadJson(seedJson(selectedRevision.definition.sourcePayloadSchema));
    setSelectedRoles([]);
    setEvents([]);
    setResult(undefined);
    setSelectedTraceIndex(0);
    setError(undefined);
  }, [bindingId, revision, selectedRevision?.id]);

  const contextualEvents = useMemo(() => {
    if (!selectedRevision) return [];
    const aliases = selectedRevision.definition.eventAliases || {};
    const canonical = new Set([
      ...Object.keys(aliases),
      ...Object.keys(selectedRevision.definition.eventInputMappings || {}),
      ...Object.keys(selectedRevision.definition.eventSourcePayloadSchemas || {})
    ]);
    return Array.from(canonical)
      .map(event => aliases[event] || event)
      .sort((left, right) => left.localeCompare(right));
  }, [selectedRevision?.id]);

  const knownRoles = useMemo(() => {
    const configured = Object.values(selectedRevision?.definition.roleOverrides || {});
    const returned = result?.availableRoles || [];
    return Array.from(new Set([...configured, ...returned])).sort((left, right) => left.localeCompare(right));
  }, [selectedRevision?.id, result]);

  const addEvent = () => {
    const eventType = contextualEvents[0] || '';
    setEvents(current => [
      ...current,
      {
        eventType,
        payloadJson: seedEventPayload(eventType),
        roles: ''
      }
    ]);
  };

  const seedEventPayload = (contextualEventType: string) => {
    if (!selectedRevision) return emptyObjectJson;
    const canonical = Object.entries(selectedRevision.definition.eventAliases || {})
      .find(([, alias]) => alias.toLowerCase() === contextualEventType.toLowerCase())?.[0] || contextualEventType;
    const schema = selectedRevision.definition.eventSourcePayloadSchemas?.[canonical]
      || selectedRevision.definition.sourcePayloadSchema;
    return seedJson(schema);
  };

  const updateEvent = (index: number, patch: Partial<EditableEvent>) => {
    setEvents(current => current.map((event, eventIndex) =>
      eventIndex === index ? { ...event, ...patch } : event
    ));
  };

  const run = async () => {
    if (!selectedBinding || !selectedRevision) return;
    setBusy(true);
    setError(undefined);
    try {
      const simulationEvents: WorkflowContextSimulationEventRequest[] = events.map((event, index) => {
        if (!event.eventType.trim()) throw new Error(`Event ${index + 1} requires an event type.`);
        const eventRoles = event.roles
          .split(',')
          .map(item => item.trim())
          .filter(Boolean);
        return {
          eventType: event.eventType.trim(),
          payload: parseObject(event.payloadJson, `Event ${index + 1} payload`),
          roles: eventRoles.length ? eventRoles : undefined
        };
      });
      const nextResult = await api.simulateContextBinding({
        contextBindingId: selectedBinding.id,
        revision,
        initialPayload: parseObject(initialPayloadJson, 'Initial payload'),
        roles: selectedRoles,
        events: simulationEvents,
        maxSteps
      }, role);
      setResult(nextResult);
      const deniedIndex = nextResult.trace.findIndex(item => !item.isAllowed);
      setSelectedTraceIndex(deniedIndex >= 0 ? deniedIndex : Math.max(nextResult.trace.length - 1, 0));
    } catch (err: any) {
      setError(err.message || 'Simulation failed.');
    } finally {
      setBusy(false);
    }
  };

  const reset = () => {
    setInitialPayloadJson(seedJson(selectedRevision?.definition.sourcePayloadSchema));
    setSelectedRoles([]);
    setEvents([]);
    setResult(undefined);
    setSelectedTraceIndex(0);
    setError(undefined);
  };

  const copyScenario = async () => {
    if (!selectedBinding) return;
    await navigator.clipboard.writeText(JSON.stringify({
      contextBindingId: selectedBinding.id,
      revision,
      initialPayload: parseObject(initialPayloadJson, 'Initial payload'),
      roles: selectedRoles,
      events: events.map(event => ({
        eventType: event.eventType,
        payload: parseObject(event.payloadJson, 'Event payload'),
        roles: event.roles.split(',').map(item => item.trim()).filter(Boolean) || undefined
      })),
      maxSteps
    }, null, 2));
    setCopied(true);
    setTimeout(() => setCopied(false), 1600);
  };

  const startRealWorkflow = async () => {
    if (!selectedBinding || revision !== 'active' || !result) return;
    if (!window.confirm('Start a real workflow with this initial source payload? This leaves simulation mode and creates persisted runtime data.')) return;
    setStarting(true);
    try {
      const started = await api.startWorkflowByContext(
        { contextBindingId: selectedBinding.id },
        parseObject(initialPayloadJson, 'Initial payload'),
        undefined,
        role
      );
      window.alert(`Workflow started: ${started.workflowInstanceId}`);
    } catch (err: any) {
      setError(err.message || 'Failed to start workflow.');
    } finally {
      setStarting(false);
    }
  };

  const selectedTrace = result?.trace[selectedTraceIndex];
  const completedSteps = result?.trace
    .filter(item => item.isAllowed)
    .map(item => item.fromStepId) || [];

  return (
    <div className="space-y-5">
      <section className="rounded-2xl border border-cyan-500/25 bg-gradient-to-r from-cyan-950/40 via-slate-900 to-indigo-950/30 p-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <div className="flex items-center gap-2 text-cyan-300">
              <FlaskConical size={18} />
              <span className="text-xs font-bold uppercase tracking-[0.18em]">Business-Context Simulation Studio</span>
            </div>
            <h2 className="mt-2 text-xl font-bold text-white">Test the binding users will actually run</h2>
            <p className="mt-1 max-w-3xl text-xs leading-5 text-slate-400">
              Uses the same mappings, aliases, roles, conditions, decision providers, workflow graph, and state-machine law as production.
              External actions and persistence are always suppressed.
            </p>
          </div>
          <div className="flex items-center gap-2 rounded-full border border-emerald-500/30 bg-emerald-500/10 px-3 py-1.5 text-[11px] font-semibold text-emerald-300">
            <ShieldCheck size={14} /> Side effects suppressed
          </div>
        </div>
      </section>

      {error && (
        <div className="flex items-start gap-2 rounded-xl border border-rose-700 bg-rose-950/40 p-3 text-xs text-rose-200">
          <AlertTriangle size={15} className="mt-0.5 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      <div className="grid grid-cols-1 gap-5 xl:grid-cols-[390px_1fr]">
        <section className="space-y-4 rounded-2xl border border-slate-700 bg-slate-900 p-4">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-bold text-white">1. Choose runtime context</h3>
            <button onClick={loadBindings} className="rounded-lg bg-slate-800 p-2 text-slate-300" title="Refresh bindings">
              <RefreshCw size={13} className={busy ? 'animate-spin' : ''} />
            </button>
          </div>
          <select
            value={bindingId}
            onChange={event => setBindingId(event.target.value)}
            className="w-full rounded-lg border border-slate-700 bg-slate-950 p-2.5 text-xs text-white"
          >
            <option value="">Select context binding</option>
            {bindings.map(binding => (
              <option key={binding.id} value={binding.id}>{binding.name} · {binding.contextType}</option>
            ))}
          </select>
          <div className="grid grid-cols-2 gap-2">
            {(['draft', 'active'] as const).map(kind => {
              const available = kind === 'draft'
                ? Boolean(selectedBinding?.draftRevision)
                : Boolean(selectedBinding?.activeRevision);
              return (
                <button
                  key={kind}
                  onClick={() => available && setRevision(kind)}
                  disabled={!available}
                  className={`rounded-lg border px-3 py-2 text-xs font-semibold ${
                    revision === kind
                      ? 'border-cyan-500 bg-cyan-500/15 text-cyan-200'
                      : 'border-slate-700 bg-slate-950 text-slate-400'
                  } disabled:cursor-not-allowed disabled:opacity-35`}
                >
                  {kind === 'draft' ? 'Saved draft' : 'Pinned active'}
                </button>
              );
            })}
          </div>

          {selectedRevision && (
            <div className="space-y-2 rounded-xl border border-slate-800 bg-slate-950/70 p-3 text-[11px]">
              <div className="flex justify-between"><span className="text-slate-500">Template</span><span className="text-slate-200">{selectedRevision.sourceWorkflowClassId.slice(0, 8)}…</span></div>
              <div className="flex justify-between"><span className="text-slate-500">Template version</span><span className="text-slate-200">{selectedRevision.sourceWorkflowClassVersion}</span></div>
              <div className="flex justify-between"><span className="text-slate-500">Binding revision</span><span className="text-slate-200">r{selectedRevision.revision} · {selectedRevision.status}</span></div>
              <div className="flex justify-between"><span className="text-slate-500">Entity type</span><span className="text-cyan-300">{selectedRevision.definition.entityType}</span></div>
            </div>
          )}

          <div>
            <div className="mb-2 flex items-center justify-between">
              <h3 className="text-sm font-bold text-white">2. Initial source payload</h3>
              <span className="text-[10px] text-slate-500">Seeded from schema</span>
            </div>
            <textarea
              value={initialPayloadJson}
              onChange={event => setInitialPayloadJson(event.target.value)}
              spellCheck={false}
              className="min-h-[180px] w-full rounded-xl border border-slate-700 bg-slate-950 p-3 font-mono text-[11px] text-slate-200"
            />
          </div>

          <div>
            <h3 className="mb-2 text-sm font-bold text-white">3. Simulated roles</h3>
            <div className="flex flex-wrap gap-2">
              {knownRoles.length === 0 && <span className="text-[11px] text-slate-500">No mapped roles declared.</span>}
              {knownRoles.map(roleName => {
                const selected = selectedRoles.includes(roleName);
                return (
                  <button
                    key={roleName}
                    onClick={() => setSelectedRoles(current =>
                      selected ? current.filter(item => item !== roleName) : [...current, roleName]
                    )}
                    className={`rounded-full border px-2.5 py-1 text-[10px] font-semibold ${
                      selected
                        ? 'border-indigo-500 bg-indigo-500/20 text-indigo-200'
                        : 'border-slate-700 bg-slate-950 text-slate-400'
                    }`}
                  >
                    {roleName}
                  </button>
                );
              })}
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={run}
              disabled={busy || !selectedRevision}
              className="flex flex-1 items-center justify-center gap-2 rounded-xl bg-cyan-600 px-4 py-2.5 text-xs font-bold text-white hover:bg-cyan-500 disabled:opacity-40"
            >
              <Play size={14} /> {busy ? 'Simulating…' : 'Run simulation'}
            </button>
            <button onClick={reset} className="rounded-xl border border-slate-700 p-2.5 text-slate-300" title="Reset scenario"><RotateCcw size={14} /></button>
            <button onClick={copyScenario} disabled={!selectedRevision} className="rounded-xl border border-slate-700 p-2.5 text-slate-300" title="Copy scenario"><Clipboard size={14} /></button>
          </div>
          {copied && <div className="text-center text-[10px] text-emerald-300">Scenario copied.</div>}
        </section>

        <div className="space-y-5">
          <section className="rounded-2xl border border-slate-700 bg-slate-900 p-4">
            <div className="mb-3 flex items-center justify-between">
              <div>
                <h3 className="text-sm font-bold text-white">4. Compose contextual events</h3>
                <p className="mt-1 text-[11px] text-slate-500">Each payload uses its event-specific source schema; role overrides are comma-separated.</p>
              </div>
              <button onClick={addEvent} disabled={!selectedRevision} className="flex items-center gap-1.5 rounded-lg bg-indigo-600 px-3 py-2 text-xs font-semibold text-white disabled:opacity-40">
                <Plus size={13} /> Add event
              </button>
            </div>
            <div className="space-y-3">
              {events.map((event, index) => (
                <div key={index} className="rounded-xl border border-slate-700 bg-slate-950/70 p-3">
                  <div className="mb-2 flex items-center gap-2">
                    <span className="flex h-5 w-5 items-center justify-center rounded-full bg-indigo-500/20 text-[10px] font-bold text-indigo-300">{index + 1}</span>
                    <input
                      list={`context-events-${index}`}
                      value={event.eventType}
                      onChange={change => updateEvent(index, {
                        eventType: change.target.value,
                        payloadJson: seedEventPayload(change.target.value)
                      })}
                      placeholder="Contextual event ID"
                      className="flex-1 rounded-lg border border-slate-700 bg-slate-900 px-2.5 py-2 font-mono text-[11px] text-white"
                    />
                    <datalist id={`context-events-${index}`}>
                      {contextualEvents.map(item => <option key={item} value={item} />)}
                    </datalist>
                    <button onClick={() => setEvents(current => current.filter((_, itemIndex) => itemIndex !== index))} className="p-2 text-slate-500 hover:text-rose-300"><Trash2 size={13} /></button>
                  </div>
                  <div className="grid grid-cols-1 gap-2 md:grid-cols-[1fr_180px]">
                    <textarea
                      value={event.payloadJson}
                      onChange={change => updateEvent(index, { payloadJson: change.target.value })}
                      spellCheck={false}
                      className="min-h-[95px] rounded-lg border border-slate-800 bg-slate-950 p-2.5 font-mono text-[10px] text-slate-300"
                    />
                    <input
                      value={event.roles}
                      onChange={change => updateEvent(index, { roles: change.target.value })}
                      placeholder="Role overrides"
                      className="h-9 rounded-lg border border-slate-800 bg-slate-950 px-2.5 text-[10px] text-slate-300"
                    />
                  </div>
                </div>
              ))}
              {events.length === 0 && (
                <div className="rounded-xl border border-dashed border-slate-700 p-6 text-center text-xs text-slate-500">
                  Add business events to advance beyond the initial step.
                </div>
              )}
            </div>
            <div className="mt-3 flex items-center gap-2 text-[11px] text-slate-500">
              <span>Maximum event steps</span>
              <input type="number" min={1} max={100} value={maxSteps} onChange={event => setMaxSteps(Number(event.target.value))} className="w-16 rounded border border-slate-700 bg-slate-950 px-2 py-1 text-white" />
            </div>
          </section>

          {selectedRevision && (
            <section className="grid grid-cols-1 gap-3 rounded-2xl border border-slate-700 bg-slate-900 p-4 md:grid-cols-2">
              <div>
                <h3 className="text-xs font-bold uppercase tracking-wider text-slate-300">Event aliases</h3>
                <div className="mt-2 space-y-1.5">
                  {Object.entries(selectedRevision.definition.eventAliases || {}).map(([canonical, contextual]) => (
                    <div key={canonical} className="flex items-center gap-2 rounded-lg bg-slate-950 px-2.5 py-2 font-mono text-[10px]">
                      <span className="text-slate-500">{canonical}</span><ChevronRight size={11} className="text-slate-600" /><span className="text-cyan-300">{contextual}</span>
                    </div>
                  ))}
                  {!Object.keys(selectedRevision.definition.eventAliases || {}).length && <p className="text-[11px] text-slate-500">Template event IDs pass through unchanged.</p>}
                </div>
              </div>
              <div>
                <h3 className="text-xs font-bold uppercase tracking-wider text-slate-300">Source → canonical projection</h3>
                <div className="mt-2 space-y-1.5">
                  {Object.entries(selectedRevision.definition.inputMapping || {}).map(([canonical, source]) => (
                    <div key={canonical} className="flex items-center gap-2 rounded-lg bg-slate-950 px-2.5 py-2 font-mono text-[10px]">
                      <span className="text-amber-300">{source}</span><ChevronRight size={11} className="text-slate-600" /><span className="text-emerald-300">{canonical}</span>
                    </div>
                  ))}
                  {Object.entries(selectedRevision.definition.conditionParameters || {}).map(([canonical, value]) => (
                    <div key={canonical} className="rounded-lg bg-slate-950 px-2.5 py-2 font-mono text-[10px] text-purple-300">
                      {canonical} = {JSON.stringify(value)} <span className="text-slate-600">(immutable)</span>
                    </div>
                  ))}
                </div>
              </div>
            </section>
          )}

          {result && (
            <>
              <section className="rounded-2xl border border-slate-700 bg-slate-900 p-4">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <div className="flex items-center gap-2">
                      {result.status === 'Denied'
                        ? <AlertTriangle size={17} className="text-rose-400" />
                        : <CheckCircle2 size={17} className="text-emerald-400" />}
                      <h3 className="text-base font-bold text-white">{result.status}</h3>
                      <span className="rounded-full border border-slate-700 px-2 py-0.5 text-[10px] text-slate-400">{result.revisionKind} r{result.revision}</span>
                    </div>
                    <p className="mt-1 text-[11px] text-slate-500">Final step {result.currentStepId} · state {result.currentState} · runtime persisted: {result.isPersistedRuntime ? 'yes' : 'no'}</p>
                  </div>
                  {result.revisionKind === 'active' && (
                    <button onClick={startRealWorkflow} disabled={starting} className="flex items-center gap-2 rounded-xl bg-emerald-600 px-3.5 py-2 text-xs font-bold text-white disabled:opacity-50">
                      <Zap size={14} /> {starting ? 'Starting…' : 'Start real workflow with this payload'}
                    </button>
                  )}
                </div>
              </section>

              <section className="rounded-2xl border border-slate-700 bg-slate-900 p-4">
                <WorkflowGraphVisualizer
                  definition={result.graph}
                  currentStepId={result.currentStepId}
                  currentState={result.currentState}
                  instanceStatus={result.status}
                  completedSteps={completedSteps}
                  initialView="both"
                />
              </section>

              <section className="grid grid-cols-1 gap-4 rounded-2xl border border-slate-700 bg-slate-900 p-4 lg:grid-cols-[300px_1fr]">
                <div>
                  <h3 className="mb-3 text-sm font-bold text-white">Transition timeline</h3>
                  <div className="max-h-[430px] space-y-2 overflow-y-auto pr-1">
                    {result.trace.map((item, index) => (
                      <button
                        key={`${item.index}-${item.eventType}`}
                        onClick={() => setSelectedTraceIndex(index)}
                        className={`w-full rounded-xl border p-3 text-left ${
                          selectedTraceIndex === index
                            ? 'border-cyan-500 bg-cyan-500/10'
                            : item.isAllowed
                              ? 'border-slate-700 bg-slate-950/60'
                              : 'border-rose-700 bg-rose-950/25'
                        }`}
                      >
                        <div className="flex items-center justify-between gap-2">
                          <span className="font-mono text-[11px] font-bold text-slate-200">{item.eventType}</span>
                          <span className={item.isAllowed ? 'text-emerald-400' : 'text-rose-400'}>{item.isAllowed ? '✓' : '×'}</span>
                        </div>
                        <div className="mt-1 text-[10px] text-slate-500">{item.fromStepId} → {item.toStepId}</div>
                        <div className="mt-1 text-[10px] text-slate-600">{item.fromState} → {item.toState}</div>
                      </button>
                    ))}
                  </div>
                </div>

                {selectedTrace && (
                  <div className="space-y-4">
                    <div className={`rounded-xl border p-3 text-xs ${selectedTrace.isAllowed ? 'border-emerald-700 bg-emerald-950/20 text-emerald-200' : 'border-rose-700 bg-rose-950/30 text-rose-200'}`}>
                      <div className="font-bold">{selectedTrace.outcome}</div>
                      {selectedTrace.reason && <div className="mt-1 leading-5">{selectedTrace.reason}</div>}
                      <div className="mt-2 text-[10px] opacity-70">Contextual: {selectedTrace.eventType} · Canonical: {selectedTrace.canonicalEventType || '—'} · Roles: {selectedTrace.roles.join(', ') || 'none'}</div>
                    </div>
                    <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
                      {[
                        ['Context before', selectedTrace.contextBefore],
                        ['Canonical delta', selectedTrace.canonicalDelta],
                        ['Context after', selectedTrace.contextAfter]
                      ].map(([label, value]) => (
                        <div key={label as string} className="min-w-0">
                          <div className="mb-1.5 text-[10px] font-bold uppercase tracking-wider text-slate-500">{label as string}</div>
                          <pre className="max-h-64 overflow-auto rounded-xl border border-slate-800 bg-slate-950 p-3 text-[10px] text-slate-300">{formatJson(value)}</pre>
                        </div>
                      ))}
                    </div>
                    <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                      <div>
                        <h4 className="mb-2 text-xs font-bold text-white">Would execute</h4>
                        <div className="space-y-2">
                          {selectedTrace.plannedActions.map((action, index) => (
                            <div key={index} className="rounded-lg border border-amber-700/40 bg-amber-950/20 p-2.5 text-[10px] text-amber-200">
                              {action.phase} · {action.actionType} {action.target ? `→ ${action.target}` : ''}
                            </div>
                          ))}
                          {!selectedTrace.plannedActions.length && <span className="text-[11px] text-slate-500">No external lifecycle actions.</span>}
                        </div>
                      </div>
                      <div>
                        <h4 className="mb-2 text-xs font-bold text-white">Pending / planned work</h4>
                        <div className="space-y-2">
                          {selectedTrace.pendingWork.map((work, index) => (
                            <div key={index} className="rounded-lg border border-indigo-700/40 bg-indigo-950/20 p-2.5 text-[10px] text-indigo-200">
                              <div className="font-bold">{work.kind} · {work.stepId}</div>
                              <div className="mt-1 text-indigo-300/70">{work.description}</div>
                            </div>
                          ))}
                          {!selectedTrace.pendingWork.length && <span className="text-[11px] text-slate-500">No pending work at this point.</span>}
                        </div>
                      </div>
                    </div>
                  </div>
                )}
              </section>
            </>
          )}
        </div>
      </div>
    </div>
  );
};
