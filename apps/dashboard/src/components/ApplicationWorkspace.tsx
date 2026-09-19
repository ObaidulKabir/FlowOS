import React, { useEffect, useMemo, useRef, useState } from 'react';
import { Archive, Brain, CheckCircle, FileText, Link2, Play, Plus, Send, Sparkles, Trash2 } from 'lucide-react';
import { WorkflowClass, WorkflowClassStatus, WorkflowInstance } from '../types';
import { ContextBindingsView } from './ContextBindingsView';
import { AiContextView } from './AiContextView';

interface Props {
  tenantName: string;
  blueprints: WorkflowClass[];
  instances: WorkflowInstance[];
  onView: (id: string) => void;
  onEdit: (id: string) => void;
  onCreate: () => void;
  onDelete: (id: string) => Promise<void>;
  onPublish: (id: string) => Promise<void>;
  onSubmit: (id: string) => Promise<void>;
  onWithdraw: (id: string) => Promise<void>;
  onDeprecate: (id: string) => Promise<void>;
  onAbandon: (id: string) => Promise<void>;
  onNewVersion: (id: string) => Promise<void>;
  onSimulate: (bindingId: string, revision: 'draft' | 'active') => void;
  onLaunch: () => void;
  onOpenSimulator?: () => void;
}

const pick = (obj: any, ...keys: string[]) => {
  if (!obj || typeof obj !== 'object') return undefined;
  for (const key of keys) {
    if (obj[key] !== undefined) return obj[key];
    const match = Object.keys(obj).find(k => k.toLowerCase() === key.toLowerCase());
    if (match) return obj[match];
  }
  return undefined;
};

const stepIds = (workflow?: WorkflowClass): string[] => {
  const steps = pick(pick(workflow?.definition, 'Workflow', 'workflow'), 'Steps', 'steps');
  if (!Array.isArray(steps)) return [];
  return steps.map((step: any) => String(pick(step, 'stepId', 'StepId') || '')).filter(Boolean);
};

const matchesFilter = (item: WorkflowClass, filter: 'All' | 'Published' | 'Drafts' | 'Shared') => {
  if (filter === 'All') return true;
  if (filter === 'Published') return item.status === WorkflowClassStatus.Published || item.status === WorkflowClassStatus.Public;
  if (filter === 'Drafts') return item.status === WorkflowClassStatus.Draft;
  return item.status === WorkflowClassStatus.Shared;
};

export const ApplicationWorkspace: React.FC<Props> = ({
  tenantName,
  blueprints,
  instances,
  onView,
  onEdit,
  onCreate,
  onDelete,
  onPublish,
  onSubmit,
  onWithdraw,
  onDeprecate,
  onAbandon,
  onNewVersion,
  onSimulate,
  onLaunch,
  onOpenSimulator
}) => {
  const [selectedId, setSelectedId] = useState<string>();
  const [listFilter, setListFilter] = useState<'All' | 'Published' | 'Drafts' | 'Shared'>('All');
  const businessContextRef = useRef<HTMLElement>(null);

  const filtered = useMemo(
    () => blueprints.filter(item => matchesFilter(item, listFilter)),
    [blueprints, listFilter]
  );

  const selected = useMemo(
    () => filtered.find(item => item.id === selectedId) || filtered[0] || blueprints.find(item => item.id === selectedId),
    [filtered, selectedId, blueprints]
  );

  useEffect(() => {
    if (!selected) return;
    if (selectedId !== selected.id) setSelectedId(selected.id);
  }, [selected, selectedId]);

  const relatedInstances = useMemo(() => {
    if (!selected) return [];
    return instances.filter(item =>
      item.workflowClassId === selected.id ||
      item.workflowClassName === selected.name
    );
  }, [instances, selected]);

  const publishedForBindings = blueprints.filter(
    item => item.status === WorkflowClassStatus.Published || item.status === WorkflowClassStatus.Public
  );
  const steps = selected ? stepIds(selected) : [];

  const scrollToBusinessContext = () => {
    businessContextRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <div className="space-y-5">
      <div className="rounded-2xl border border-blue-500/30 bg-gradient-to-r from-blue-950/50 via-slate-900 to-violet-950/40 p-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <div className="flex items-center gap-2 mb-1">
              <FileText className="text-blue-300" size={18} />
              <h3 className="text-lg font-bold text-white">Application</h3>
            </div>
            <p className="text-xs text-slate-400 max-w-3xl">
              One application is a <strong className="text-blue-300">Workflow</strong> template,
              bound to a <strong className="text-amber-300">Business Context</strong> (Data),
              composed with <strong className="text-violet-300">AI Context</strong> (Prompt + Tools + Provider).
              Select a workflow and the three stay in the same space.
            </p>
          </div>
          <div className="flex gap-2">
            <button onClick={onCreate} className="px-3 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-xl text-xs font-semibold flex items-center gap-1.5">
              <Plus size={14} /> New workflow
            </button>
            {onOpenSimulator && (
              <button type="button" onClick={onOpenSimulator} className="px-3 py-2 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white rounded-xl text-xs font-bold flex items-center gap-1.5 shadow-md shadow-emerald-500/20">
                <Sparkles size={14} /> Visual Demo Simulator
              </button>
            )}
            <button onClick={onLaunch} className="px-3 py-2 bg-slate-800 hover:bg-slate-700 border border-slate-600 text-slate-200 rounded-xl text-xs font-semibold flex items-center gap-1.5">
              <Play size={14} /> Launch Instance
            </button>
          </div>
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-2 text-[11px] font-semibold">
          <span className="px-2.5 py-1 rounded-full bg-blue-500/20 text-blue-200 border border-blue-500/30 flex items-center gap-1">
            <FileText size={12} /> Workflow
          </span>
          <span className="text-slate-500">→</span>
          <span className="px-2.5 py-1 rounded-full bg-amber-500/20 text-amber-200 border border-amber-500/30 flex items-center gap-1">
            <Link2 size={12} /> Business Context
          </span>
          <span className="text-slate-500">→</span>
          <span className="px-2.5 py-1 rounded-full bg-violet-500/20 text-violet-200 border border-violet-500/30 flex items-center gap-1">
            <Brain size={12} /> AI Context
          </span>
        </div>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-[280px_1fr] gap-5">
        <section className="bg-slate-900 border border-slate-700 rounded-2xl overflow-hidden h-fit">
          <div className="p-3 border-b border-slate-800 space-y-2">
            <div className="text-[11px] uppercase tracking-wide text-slate-500 font-bold">
              Applications ({filtered.length})
            </div>
            <div className="flex bg-slate-950 rounded-lg p-1 border border-slate-800 text-[11px]">
              {(['All', 'Published', 'Drafts', 'Shared'] as const).map(sub => (
                <button
                  key={sub}
                  onClick={() => setListFilter(sub)}
                  className={`flex-1 px-1.5 py-1 rounded-md ${listFilter === sub ? 'bg-blue-600 text-white' : 'text-slate-400'}`}
                >
                  {sub}
                </button>
              ))}
            </div>
          </div>
          <div className="max-h-[640px] overflow-y-auto divide-y divide-slate-800">
            {filtered.map(item => (
              <button
                key={item.id}
                onClick={() => setSelectedId(item.id)}
                className={`w-full text-left p-3 ${selected?.id === item.id ? 'bg-blue-600/20' : 'hover:bg-slate-800/70'}`}
              >
                <div className="text-sm font-semibold text-white truncate">{item.name}</div>
                <div className="text-[10px] text-slate-500 mt-0.5">v{item.version} · {WorkflowClassStatus[item.status] ?? item.status}</div>
              </button>
            ))}
            {filtered.length === 0 && (
              <div className="p-6 text-xs text-slate-500 text-center">No workflows in this filter. Create a blueprint to start an application.</div>
            )}
          </div>
        </section>

        <div className="space-y-5 min-w-0">
          {!selected ? (
            <div className="text-sm text-slate-400 border border-dashed border-slate-700 rounded-2xl p-10 text-center">
              Create or select a workflow to bind Business Context and AI Context.<br />
              <button
                onClick={onOpenSimulator || onLaunch}
                className="mt-4 px-4 py-2 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white rounded-xl text-sm font-bold shadow-lg shadow-emerald-500/20 inline-flex items-center gap-2 transition-all"
              >
                <Sparkles size={16} /> Try Visual Demo Simulator
              </button>
              <p className="mt-2 text-[11px] text-slate-500">In-memory graph walk — no live instance is created.</p>
            </div>
          ) : (
            <>
              <section className="rounded-2xl border border-blue-500/25 bg-slate-900 p-4 space-y-3">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <div className="text-[10px] uppercase tracking-wide text-blue-400 font-bold flex items-center gap-1.5">
                      <FileText size={12} /> Workflow
                    </div>
                    <h4 className="text-lg font-bold text-white mt-0.5">{selected.name} <span className="text-slate-500 text-sm font-mono">v{selected.version}</span></h4>
                    <p className="text-[11px] text-slate-500 mt-1">
                      {WorkflowClassStatus[selected.status] ?? selected.status}
                      {steps.length ? ` · ${steps.length} steps · ${steps.slice(0, 6).join(', ')}${steps.length > 6 ? '…' : ''}` : ''}
                      {' · '}{relatedInstances.length} live instance{relatedInstances.length === 1 ? '' : 's'}
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <button onClick={() => onView(selected.id)} className="px-3 py-1.5 rounded-lg bg-slate-800 text-xs text-slate-200">View</button>
                    {(selected.status === WorkflowClassStatus.Draft) && (
                      <>
                        <button onClick={() => onEdit(selected.id)} className="px-3 py-1.5 rounded-lg bg-blue-600 text-xs text-white">Edit</button>
                        <button onClick={() => onPublish(selected.id)} className="px-3 py-1.5 rounded-lg bg-emerald-600 text-xs text-white inline-flex items-center gap-1"><CheckCircle size={12} /> Publish</button>
                        <button onClick={() => onDelete(selected.id)} className="px-3 py-1.5 rounded-lg bg-rose-700 text-xs text-white inline-flex items-center gap-1"><Trash2 size={12} /> Delete</button>
                      </>
                    )}
                    {(selected.status === WorkflowClassStatus.Published || selected.status === WorkflowClassStatus.Public) && (
                      <>
                        <button onClick={() => onSubmit(selected.id)} className="px-3 py-1.5 rounded-lg bg-violet-600 text-xs text-white inline-flex items-center gap-1"><Send size={12} /> Submit</button>
                        <button onClick={() => onNewVersion(selected.id)} className="px-3 py-1.5 rounded-lg bg-teal-700 text-xs text-white">New version</button>
                        <button onClick={() => onDeprecate(selected.id)} className="px-3 py-1.5 rounded-lg bg-amber-700 text-xs text-white inline-flex items-center gap-1"><Archive size={12} /> Deprecate</button>
                        <button onClick={() => onAbandon(selected.id)} className="px-3 py-1.5 rounded-lg bg-rose-700 text-xs text-white">Abandon</button>
                      </>
                    )}
                    {selected.status === WorkflowClassStatus.Shared && (
                      <button onClick={() => onWithdraw(selected.id)} className="px-3 py-1.5 rounded-lg bg-amber-700 text-xs text-white">Withdraw</button>
                    )}
                  </div>
                </div>
              </section>

              <section ref={businessContextRef} className="rounded-2xl border border-amber-500/25 bg-slate-900 p-4">
                <ContextBindingsView
                  compact
                  filterSourceWorkflowClassId={selected.id}
                  workflowClasses={publishedForBindings.length ? publishedForBindings : [selected]}
                  role="Tenant"
                  onSimulate={onSimulate}
                />
              </section>

              <section className="rounded-2xl border border-violet-500/25 bg-slate-900 p-4">
                <AiContextView
                  compact
                  tenantName={tenantName}
                  workflowClass={selected}
                  onOpenBusinessContext={scrollToBusinessContext}
                />
              </section>
            </>
          )}
        </div>
      </div>
    </div>
  );
};
