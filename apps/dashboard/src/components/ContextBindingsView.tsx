import React, { useEffect, useMemo, useState } from 'react';
import { api } from '../api/client';
import {
  ValidationResult,
  WorkflowClass,
  WorkflowContextBinding,
  WorkflowContextBindingDefinition
} from '../types';
import { Archive, CheckCircle2, Link2, Play, Plus, RefreshCw, Save } from 'lucide-react';

const emptyDefinition: WorkflowContextBindingDefinition = {
  entityType: 'ApprovalSubject',
  eventAliases: {},
  roleOverrides: {},
  capabilityOverrides: {},
  inputMapping: {},
  eventInputMappings: {},
  conditionParameters: {},
  decisionProviderOverrides: {},
  eventSourcePayloadSchemas: {},
  metadata: {}
};

interface Props {
  workflowClasses: WorkflowClass[];
  role?: 'Tenant' | 'Admin';
}

export const ContextBindingsView: React.FC<Props> = ({ workflowClasses, role = 'Tenant' }) => {
  const [bindings, setBindings] = useState<WorkflowContextBinding[]>([]);
  const [selectedId, setSelectedId] = useState<string>();
  const [definitionJson, setDefinitionJson] = useState(JSON.stringify(emptyDefinition, null, 2));
  const [validation, setValidation] = useState<ValidationResult>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [showCreate, setShowCreate] = useState(false);
  const [sourceWorkflowClassId, setSourceWorkflowClassId] = useState('');
  const [contextType, setContextType] = useState('');
  const [name, setName] = useState('');

  const selected = useMemo(
    () => bindings.find(binding => binding.id === selectedId),
    [bindings, selectedId]
  );

  const load = async () => {
    setBusy(true);
    setError(undefined);
    try {
      const result = await api.listContextBindings(undefined, role);
      setBindings(result);
      if (!selectedId && result.length) setSelectedId(result[0].id);
    } catch (err: any) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  useEffect(() => {
    const draft = selected?.draftRevision?.definition;
    const active = selected?.activeRevision?.definition;
    if (draft || active) setDefinitionJson(JSON.stringify(draft || active, null, 2));
    setValidation(undefined);
  }, [selectedId, selected?.draftRevisionId, selected?.activeRevisionId]);

  const parseDefinition = (): WorkflowContextBindingDefinition => JSON.parse(definitionJson);

  const create = async () => {
    setBusy(true);
    try {
      const created = await api.createContextBinding({
        sourceWorkflowClassId,
        contextType,
        name,
        definition: parseDefinition()
      }, role);
      setShowCreate(false);
      await load();
      setSelectedId(created.id);
    } catch (err: any) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    if (!selected) return;
    setBusy(true);
    try {
      await api.updateContextBindingDraft(selected.id, parseDefinition(), undefined, role);
      await load();
    } catch (err: any) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const validate = async () => {
    if (!selected) return;
    setBusy(true);
    try {
      setValidation(await api.validateContextBinding(selected.id, role));
    } catch (err: any) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const activate = async () => {
    if (!selected || !window.confirm('Activate this immutable binding revision? Existing instances remain pinned.')) return;
    setBusy(true);
    try {
      await api.activateContextBinding(selected.id, role);
      await load();
    } catch (err: any) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const archive = async () => {
    if (!selected || !window.confirm('Archive this binding and block new starts?')) return;
    setBusy(true);
    try {
      await api.archiveContextBinding(selected.id, role);
      await load();
    } catch (err: any) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const start = async () => {
    if (!selected) return;
    const rawPayload = window.prompt('Source payload JSON', '{}');
    if (rawPayload === null) return;
    try {
      const result = await api.startWorkflowByContext(
        { contextBindingId: selected.id },
        JSON.parse(rawPayload),
        undefined,
        role
      );
      window.alert(`Workflow started: ${result.workflowInstanceId}`);
    } catch (err: any) {
      setError(err.message);
    }
  };

  return (
    <div className="grid grid-cols-1 xl:grid-cols-[340px_1fr] gap-5">
      <section className="bg-slate-900 border border-slate-700 rounded-2xl overflow-hidden">
        <div className="p-4 border-b border-slate-700 flex items-center justify-between">
          <div>
            <h3 className="text-sm font-bold text-white flex items-center gap-2"><Link2 size={15} /> Context Bindings</h3>
            <p className="text-[11px] text-slate-400 mt-1">Deploy reusable templates per business domain.</p>
          </div>
          <div className="flex gap-1">
            <button onClick={load} className="p-2 rounded-lg bg-slate-800 text-slate-300"><RefreshCw size={13} /></button>
            <button onClick={() => setShowCreate(true)} className="p-2 rounded-lg bg-blue-600 text-white"><Plus size={13} /></button>
          </div>
        </div>
        <div className="divide-y divide-slate-800 max-h-[580px] overflow-y-auto">
          {bindings.map(binding => (
            <button
              key={binding.id}
              onClick={() => setSelectedId(binding.id)}
              className={`w-full p-4 text-left ${selectedId === binding.id ? 'bg-blue-600/15' : 'hover:bg-slate-800/60'}`}
            >
              <div className="flex justify-between gap-2">
                <span className="text-sm font-semibold text-slate-100">{binding.name}</span>
                <span className="text-[10px] uppercase text-blue-300">{binding.status}</span>
              </div>
              <div className="text-xs text-slate-400 mt-1">{binding.contextType}</div>
              <div className="text-[10px] text-slate-500 mt-1">Active r{binding.activeRevision?.revision ?? '—'} · Draft r{binding.draftRevision?.revision ?? '—'}</div>
            </button>
          ))}
          {!bindings.length && <div className="p-6 text-xs text-slate-500 text-center">No context bindings yet.</div>}
        </div>
      </section>

      <section className="bg-slate-900 border border-slate-700 rounded-2xl p-5 space-y-4">
        {error && <div className="p-3 rounded-lg bg-rose-900/30 border border-rose-700 text-xs text-rose-300">{error}</div>}
        {!selected ? (
          <div className="text-sm text-slate-400">Select or create a context binding.</div>
        ) : (
          <>
            <div className="flex flex-wrap justify-between gap-3">
              <div>
                <h3 className="text-lg font-bold text-white">{selected.name}</h3>
                <p className="text-xs text-slate-400">Context: {selected.contextType} · ID: <code>{selected.id}</code></p>
              </div>
              <div className="flex flex-wrap gap-2">
                <button onClick={save} disabled={busy || selected.status === 'Archived'} className="px-3 py-2 rounded-lg bg-slate-700 text-xs text-white flex gap-1.5 items-center"><Save size={13} /> Save draft</button>
                <button onClick={validate} disabled={busy} className="px-3 py-2 rounded-lg bg-indigo-600 text-xs text-white flex gap-1.5 items-center"><CheckCircle2 size={13} /> Validate</button>
                <button onClick={activate} disabled={busy || !selected.draftRevisionId} className="px-3 py-2 rounded-lg bg-emerald-600 text-xs text-white">Activate</button>
                <button onClick={start} disabled={selected.status !== 'Active'} className="px-3 py-2 rounded-lg bg-blue-600 text-xs text-white flex gap-1.5 items-center"><Play size={13} /> Start</button>
                <button onClick={archive} disabled={selected.status === 'Archived'} className="px-3 py-2 rounded-lg bg-rose-700 text-xs text-white flex gap-1.5 items-center"><Archive size={13} /> Archive</button>
              </div>
            </div>
            <textarea
              value={definitionJson}
              onChange={event => setDefinitionJson(event.target.value)}
              className="w-full min-h-[430px] rounded-xl bg-slate-950 border border-slate-700 p-4 font-mono text-xs text-slate-200"
              spellCheck={false}
            />
            {validation && (
              <div className={`p-3 rounded-lg border text-xs ${validation.isValid ? 'bg-emerald-900/20 border-emerald-700 text-emerald-300' : 'bg-amber-900/20 border-amber-700 text-amber-300'}`}>
                {validation.isValid ? 'Binding is valid and ready to activate.' : validation.errors.map(item => `${item.code}: ${item.message}`).join(' · ')}
              </div>
            )}
          </>
        )}
      </section>

      {showCreate && (
        <div className="fixed inset-0 z-50 bg-black/70 flex items-center justify-center p-5">
          <div className="w-full max-w-lg rounded-2xl bg-slate-900 border border-slate-700 p-5 space-y-4">
            <h3 className="font-bold text-white">Create Context Binding</h3>
            <select value={sourceWorkflowClassId} onChange={event => setSourceWorkflowClassId(event.target.value)} className="w-full bg-slate-950 border border-slate-700 rounded-lg p-2 text-sm text-white">
              <option value="">Select a published template</option>
              {workflowClasses.map(workflow => <option key={workflow.id} value={workflow.id}>{workflow.name} v{workflow.version}</option>)}
            </select>
            <input value={contextType} onChange={event => setContextType(event.target.value)} placeholder="Context type (Expense)" className="w-full bg-slate-950 border border-slate-700 rounded-lg p-2 text-sm text-white" />
            <input value={name} onChange={event => setName(event.target.value)} placeholder="Runtime name (ExpenseApproval)" className="w-full bg-slate-950 border border-slate-700 rounded-lg p-2 text-sm text-white" />
            <div className="flex justify-end gap-2">
              <button onClick={() => setShowCreate(false)} className="px-4 py-2 text-xs text-slate-300">Cancel</button>
              <button onClick={create} disabled={!sourceWorkflowClassId || !contextType || !name || busy} className="px-4 py-2 rounded-lg bg-blue-600 text-xs text-white">Create draft</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
