import React, { useEffect, useMemo, useState } from 'react';
import { api } from '../api/client';
import {
  ValidationResult,
  WorkflowClass,
  WorkflowContextBinding,
  WorkflowContextBindingDefinition
} from '../types';
import { Archive, CheckCircle2, FlaskConical, Link2, Play, Plus, RefreshCw, Save } from 'lucide-react';

const emptyDefinition: WorkflowContextBindingDefinition = {
  entityType: 'ApprovalSubject',
  eventAliases: {},
  roleOverrides: {},
  capabilityOverrides: {},
  roleStaticMemberOverrides: {},
  inputMapping: {},
  eventInputMappings: {},
  conditionParameters: {},
  decisionProviderOverrides: {},
  eventSourcePayloadSchemas: {},
  metadata: {}
};

const pick = (obj: any, ...keys: string[]) => {
  if (!obj || typeof obj !== 'object') return undefined;
  for (const key of keys) {
    if (obj[key] !== undefined) return obj[key];
    const match = Object.keys(obj).find(item => item.toLowerCase() === key.toLowerCase());
    if (match) return obj[match];
  }
  return undefined;
};

const asList = (value: any): any[] => Array.isArray(value) ? value : [];

const definitionFromClass = (workflow?: WorkflowClass): WorkflowContextBindingDefinition => {
  const blueprint = workflow?.definition || {};
  const roleOverrides: Record<string, string> = {};
  asList(pick(blueprint, 'Roles', 'roles')).forEach(role => {
    const name = String(pick(role, 'Name', 'name') || '').trim();
    if (name) roleOverrides[name] = name;
  });
  const capabilityOverrides: Record<string, string> = {};
  asList(pick(blueprint, 'Capabilities', 'capabilities')).forEach(capability => {
    const code = String(pick(capability, 'Code', 'code') || '').trim();
    if (code) capabilityOverrides[code] = code;
  });
  return {
    ...emptyDefinition,
    entityType: pick(pick(blueprint, 'StateMachine', 'stateMachine'), 'EntityType', 'entityType') || `${workflow?.name || 'Approval'}Entity`,
    roleOverrides,
    capabilityOverrides
  };
};

const resolveBusinessVocabulary = (
  workflow: WorkflowClass | undefined,
  definition: WorkflowContextBindingDefinition | undefined
) => {
  const blueprint = workflow?.definition || {};
  const roleOverrides = (pick(definition, 'roleOverrides', 'RoleOverrides') || {}) as Record<string, string>;
  const capabilityOverrides = (pick(definition, 'capabilityOverrides', 'CapabilityOverrides') || {}) as Record<string, string>;
  let roles = asList(pick(blueprint, 'Roles', 'roles')).map(role => {
    const template = String(pick(role, 'Name', 'name') || '').trim();
    const granted = asList(pick(role, 'GrantedCapabilities', 'grantedCapabilities'))
      .map(item => String(item || '').trim())
      .filter(Boolean)
      .map(code => capabilityOverrides[code] || code);
    return {
      template,
      name: (template && (roleOverrides[template] || pick(roleOverrides, template))) || template,
      capabilities: Array.from(new Set(granted))
    };
  }).filter(role => role.template);
  if (roles.length === 0) {
    roles = Object.entries(roleOverrides)
      .filter(([template, name]) => String(template || '').trim() && String(name || '').trim())
      .map(([template, name]) => ({ template, name: String(name), capabilities: [] as string[] }));
  }
  let capabilities = asList(pick(blueprint, 'Capabilities', 'capabilities')).map(capability => {
    const code = String(pick(capability, 'Code', 'code') || '').trim();
    return {
      template: code,
      code: (code && (capabilityOverrides[code] || pick(capabilityOverrides, code))) || code,
      description: String(pick(capability, 'Description', 'description') || '')
    };
  }).filter(capability => capability.template);
  if (capabilities.length === 0) {
    capabilities = Object.entries(capabilityOverrides)
      .filter(([template, code]) => String(template || '').trim() && String(code || '').trim())
      .map(([template, code]) => ({ template, code: String(code), description: '' }));
  }
  return { roles, capabilities };
};

interface Props {
  workflowClasses: WorkflowClass[];
  role?: 'Tenant' | 'Admin';
  onSimulate?: (bindingId: string, revision: 'draft' | 'active') => void;
  filterSourceWorkflowClassId?: string;
  compact?: boolean;
}

export const ContextBindingsView: React.FC<Props> = ({
  workflowClasses,
  role = 'Tenant',
  onSimulate,
  filterSourceWorkflowClassId,
  compact = false
}) => {
  const [bindings, setBindings] = useState<WorkflowContextBinding[]>([]);
  const [selectedId, setSelectedId] = useState<string>();
  const [definitionJson, setDefinitionJson] = useState(JSON.stringify(emptyDefinition, null, 2));
  const [validation, setValidation] = useState<ValidationResult>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [showCreate, setShowCreate] = useState(false);
  const [sourceWorkflowClassId, setSourceWorkflowClassId] = useState(filterSourceWorkflowClassId || '');
  const [contextType, setContextType] = useState('');
  const [name, setName] = useState('');

  const selected = useMemo(
    () => bindings.find(binding => binding.id === selectedId),
    [bindings, selectedId]
  );

  const selectedSourceClass = useMemo(() => {
    const sourceId = selected?.draftRevision?.sourceWorkflowClassId
      || selected?.activeRevision?.sourceWorkflowClassId
      || filterSourceWorkflowClassId;
    return workflowClasses.find(item => item.id === sourceId);
  }, [selected, workflowClasses, filterSourceWorkflowClassId]);

  const selectedVocabulary = useMemo(() => {
    try {
      return resolveBusinessVocabulary(selectedSourceClass, JSON.parse(definitionJson));
    } catch {
      const draft = selected?.draftRevision?.definition || selected?.activeRevision?.definition;
      return resolveBusinessVocabulary(selectedSourceClass, draft);
    }
  }, [selectedSourceClass, definitionJson, selected]);

  const load = async () => {
    setBusy(true);
    setError(undefined);
    try {
      const result = await api.listContextBindings(filterSourceWorkflowClassId, role);
      setBindings(result);
      setSelectedId(current => result.some(item => item.id === current) ? current : result[0]?.id);
    } catch (err: any) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    load();
  }, [filterSourceWorkflowClassId]);

  useEffect(() => {
    const draft = selected?.draftRevision?.definition;
    const active = selected?.activeRevision?.definition;
    if (draft || active) setDefinitionJson(JSON.stringify(draft || active, null, 2));
    setValidation(undefined);
  }, [selectedId, selected?.draftRevisionId, selected?.activeRevisionId]);

  useEffect(() => {
    if (filterSourceWorkflowClassId) setSourceWorkflowClassId(filterSourceWorkflowClassId);
  }, [filterSourceWorkflowClassId]);

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
    <div className={compact ? 'space-y-4' : 'grid grid-cols-1 xl:grid-cols-[340px_1fr] gap-5'}>
      <section className={compact ? 'overflow-hidden' : 'bg-slate-900 border border-slate-700 rounded-2xl overflow-hidden'}>
        <div className="p-4 border-b border-slate-700 flex items-center justify-between">
          <div>
            <h3 className="text-sm font-bold text-white flex items-center gap-2"><Link2 size={15} /> Business Context</h3>
            <p className="text-[11px] text-slate-400 mt-1">
              {filterSourceWorkflowClassId
                ? 'Bindings for the selected workflow template.'
                : 'Map a workflow template to a business domain (entity, events, payload).'}
            </p>
          </div>
          <div className="flex gap-1">
            <button onClick={load} className="p-2 rounded-lg bg-slate-800 text-slate-300"><RefreshCw size={13} /></button>
            <button
              onClick={() => {
                const source = workflowClasses.find(item => item.id === (filterSourceWorkflowClassId || sourceWorkflowClassId));
                if (source) {
                  setSourceWorkflowClassId(source.id);
                  setContextType(source.name.replace(/Flow$/, ''));
                  setName(`${source.name} Context`);
                  setDefinitionJson(JSON.stringify(definitionFromClass(source), null, 2));
                }
                setShowCreate(true);
              }}
              className="p-2 rounded-lg bg-blue-600 text-white"
            >
              <Plus size={13} />
            </button>
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
          {!bindings.length && <div className="p-6 text-xs text-slate-500 text-center">No business contexts yet.</div>}
        </div>
      </section>

      <section className={compact ? 'space-y-4' : 'bg-slate-900 border border-slate-700 rounded-2xl p-5 space-y-4'}>
        {error && <div className="p-3 rounded-lg bg-rose-900/30 border border-rose-700 text-xs text-rose-300">{error}</div>}
        {!selected ? (
          <div className="text-sm text-slate-400">Select or create a business context.</div>
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
                <button
                  onClick={() => onSimulate?.(selected.id, selected.draftRevision ? 'draft' : 'active')}
                  disabled={!onSimulate || (!selected.draftRevision && !selected.activeRevision)}
                  className="px-3 py-2 rounded-lg bg-cyan-700 text-xs text-white flex gap-1.5 items-center disabled:opacity-40"
                >
                  <FlaskConical size={13} /> Simulate
                </button>
                <button onClick={activate} disabled={busy || !selected.draftRevisionId} className="px-3 py-2 rounded-lg bg-emerald-600 text-xs text-white">Activate</button>
                <button onClick={start} disabled={selected.status !== 'Active'} className="px-3 py-2 rounded-lg bg-blue-600 text-xs text-white flex gap-1.5 items-center"><Play size={13} /> Start</button>
                <button onClick={archive} disabled={selected.status === 'Archived'} className="px-3 py-2 rounded-lg bg-rose-700 text-xs text-white flex gap-1.5 items-center"><Archive size={13} /> Archive</button>
              </div>
            </div>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
              <div className="rounded-xl border border-amber-500/20 bg-slate-950/70 p-3">
                <div className="text-[10px] uppercase tracking-wide text-amber-300 font-bold mb-2">Business roles</div>
                {selectedVocabulary.roles.length === 0 && (
                  <p className="text-[11px] text-slate-500">No roles declared on the template or this binding.</p>
                )}
                <div className="space-y-2">
                  {selectedVocabulary.roles.map(role => (
                    <div key={role.template} className="text-[11px]">
                      <div className="text-slate-100 font-semibold">
                        {role.name}
                        {role.name !== role.template && (
                          <span className="ml-1 text-slate-500 font-normal">← {role.template}</span>
                        )}
                      </div>
                      <div className="text-slate-400 break-all">{role.capabilities.join(', ') || 'no granted capabilities'}</div>
                    </div>
                  ))}
                </div>
              </div>
              <div className="rounded-xl border border-cyan-500/20 bg-slate-950/70 p-3">
                <div className="text-[10px] uppercase tracking-wide text-cyan-300 font-bold mb-2">Capabilities</div>
                {selectedVocabulary.capabilities.length === 0 && (
                  <p className="text-[11px] text-slate-500">No capabilities declared on the template or this binding.</p>
                )}
                <div className="flex flex-wrap gap-1.5">
                  {selectedVocabulary.capabilities.map(capability => (
                    <span key={capability.template} className="px-2 py-1 rounded-lg bg-slate-800 text-[10px] text-cyan-100" title={capability.description}>
                      {capability.code}
                      {capability.code !== capability.template ? ` ← ${capability.template}` : ''}
                    </span>
                  ))}
                </div>
              </div>
            </div>
            <textarea
              value={definitionJson}
              onChange={event => setDefinitionJson(event.target.value)}
              className={`w-full rounded-xl bg-slate-950 border border-slate-700 p-4 font-mono text-xs text-slate-200 ${compact ? 'min-h-[180px]' : 'min-h-[320px]'}`}
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
            <h3 className="font-bold text-white">Create Business Context</h3>
            <select
              value={sourceWorkflowClassId}
              onChange={event => {
                const nextId = event.target.value;
                setSourceWorkflowClassId(nextId);
                const source = workflowClasses.find(item => item.id === nextId);
                if (source) {
                  setContextType(current => current || source.name.replace(/Flow$/, ''));
                  setName(current => current || `${source.name} Context`);
                  setDefinitionJson(JSON.stringify(definitionFromClass(source), null, 2));
                }
              }}
              className="w-full bg-slate-950 border border-slate-700 rounded-lg p-2 text-sm text-white"
            >
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
