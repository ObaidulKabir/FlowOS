import React, { useEffect, useMemo, useState } from 'react';
import {
  Archive,
  Brain,
  CheckCircle,
  FileText,
  Link2,
  Play,
  Plus,
  Send,
  Sparkles,
  Trash2,
  Layers,
  Activity,
  Search,
  Edit2,
  History,
  Copy,
  Check
} from 'lucide-react';
import { WorkflowClass, WorkflowClassStatus, WorkflowInstance } from '../types';
import { ContextBindingsView } from './ContextBindingsView';
import { AiContextView } from './AiContextView';
import { WorkflowGraphVisualizer } from './WorkflowGraphVisualizer';

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
  onInspectInstance?: (id: string) => void;
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

const extractSteps = (workflow?: WorkflowClass): any[] => {
  const steps = pick(pick(workflow?.definition, 'Workflow', 'workflow'), 'Steps', 'steps');
  return Array.isArray(steps) ? steps : [];
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
  onOpenSimulator,
  onInspectInstance
}) => {
  const [selectedId, setSelectedId] = useState<string>();
  const [listFilter, setListFilter] = useState<'All' | 'Published' | 'Drafts' | 'Shared'>('All');
  const [searchTerm, setSearchTerm] = useState('');
  const [detailTab, setDetailTab] = useState<'graph' | 'business' | 'ai' | 'instances'>('graph');
  const [copiedId, setCopiedId] = useState<string | null>(null);

  const filtered = useMemo(() => {
    return blueprints
      .filter(item => matchesFilter(item, listFilter))
      .filter(item => {
        if (!searchTerm.trim()) return true;
        const q = searchTerm.toLowerCase();
        return item.name.toLowerCase().includes(q) || item.version.toLowerCase().includes(q);
      });
  }, [blueprints, listFilter, searchTerm]);

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

  const steps = useMemo(() => extractSteps(selected), [selected]);

  const handleCopy = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  return (
    <div className="space-y-4">
      {/* Top Banner / Studio Control Header */}
      <div className="rounded-2xl border border-blue-500/30 bg-gradient-to-r from-blue-950/50 via-slate-900 to-violet-950/40 p-4">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <div className="flex items-center gap-2">
              <FileText className="text-blue-400" size={20} />
              <h3 className="text-base font-bold text-white">
                Application Design Studio
              </h3>
              <span className="text-xs text-slate-500">•</span>
              <span className="text-xs text-slate-400 font-mono">
                {blueprints.length} Workflow{blueprints.length === 1 ? '' : 's'} defined
              </span>
            </div>
            <p className="text-xs text-slate-400 mt-1 max-w-3xl">
              An Application binds a <strong className="text-blue-300">Process Graph (Workflow)</strong> to a{' '}
              <strong className="text-amber-300">Data Contract (Business Context)</strong> and{' '}
              <strong className="text-violet-300">Governed AI Agents (AI Context)</strong>.
            </p>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={onCreate}
              className="px-3.5 py-1.5 bg-blue-600 hover:bg-blue-500 text-white rounded-xl text-xs font-semibold flex items-center gap-1.5 shadow-md shadow-blue-600/20 transition-all"
            >
              <Plus size={14} /> New Workflow
            </button>
            {onOpenSimulator && (
              <button
                type="button"
                onClick={onOpenSimulator}
                className="px-3.5 py-1.5 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white rounded-xl text-xs font-bold flex items-center gap-1.5 shadow-md shadow-emerald-500/20 transition-all"
              >
                <Sparkles size={14} /> Visual Simulator
              </button>
            )}
            <button
              onClick={onLaunch}
              className="px-3.5 py-1.5 bg-slate-800 hover:bg-slate-700 border border-slate-600 text-slate-200 rounded-xl text-xs font-semibold flex items-center gap-1.5 transition-colors"
            >
              <Play size={13} /> Launch Instance
            </button>
          </div>
        </div>
      </div>

      {/* Main Master-Detail Grid */}
      <div className="grid grid-cols-1 lg:grid-cols-[300px_1fr] gap-5">
        {/* Left Column: Workflows Catalog */}
        <section className="bg-slate-900 border border-slate-700 rounded-2xl overflow-hidden h-fit space-y-3 p-3">
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase tracking-wider text-slate-400">
                Workflows ({filtered.length})
              </span>
              <span className="text-[11px] text-slate-500 font-mono">
                {blueprints.filter(b => b.status === WorkflowClassStatus.Published || b.status === WorkflowClassStatus.Public).length} published
              </span>
            </div>

            {/* Filter Pills */}
            <div className="flex bg-slate-950 rounded-lg p-1 border border-slate-800 text-[11px]">
              {(['All', 'Published', 'Drafts', 'Shared'] as const).map(sub => (
                <button
                  key={sub}
                  onClick={() => setListFilter(sub)}
                  className={`flex-1 px-1.5 py-1 rounded-md font-medium transition-all ${
                    listFilter === sub ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
                  }`}
                >
                  {sub}
                </button>
              ))}
            </div>

            {/* Search Input */}
            <div className="relative">
              <Search size={13} className="absolute left-2.5 top-2.5 text-slate-500" />
              <input
                type="text"
                placeholder="Search workflows..."
                value={searchTerm}
                onChange={e => setSearchTerm(e.target.value)}
                className="w-full bg-slate-950 border border-slate-800 rounded-lg pl-8 pr-3 py-1.5 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500"
              />
            </div>
          </div>

          {/* Workflow List */}
          <div className="max-h-[640px] overflow-y-auto space-y-1.5 pr-0.5">
            {filtered.map(item => {
              const isSelected = selected?.id === item.id;
              const isDraft = item.status === WorkflowClassStatus.Draft;
              const isPublished = item.status === WorkflowClassStatus.Published || item.status === WorkflowClassStatus.Public;
              const statusName = WorkflowClassStatus[item.status] ?? item.status;
              const itemInstances = instances.filter(i => i.workflowClassId === item.id || i.workflowClassName === item.name).length;

              return (
                <button
                  key={item.id}
                  onClick={() => setSelectedId(item.id)}
                  className={`w-full text-left p-3 rounded-xl border transition-all ${
                    isSelected
                      ? 'bg-blue-600/15 border-blue-500 ring-1 ring-blue-500/40'
                      : 'bg-slate-950/60 border-slate-800/80 hover:bg-slate-800/60 hover:border-slate-700'
                  }`}
                >
                  <div className="flex items-center justify-between gap-1">
                    <span className="font-semibold text-white text-xs truncate">{item.name}</span>
                    <span className="px-1.5 py-0.5 rounded font-mono text-[10px] bg-slate-800 text-slate-300 border border-slate-700 shrink-0">
                      v{item.version}
                    </span>
                  </div>

                  <div className="flex items-center gap-1.5 mt-2 flex-wrap text-[10px]">
                    <span className={`px-1.5 py-0.2 rounded font-medium ${
                      isPublished ? 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/30' :
                      isDraft ? 'bg-amber-500/20 text-amber-300 border border-amber-500/30' :
                      'bg-slate-800 text-slate-400'
                    }`}>
                      {statusName}
                    </span>

                    {itemInstances > 0 && (
                      <span className="px-1.5 py-0.2 rounded font-mono bg-blue-500/10 text-blue-300 border border-blue-500/20">
                        {itemInstances} live
                      </span>
                    )}

                    {item.flowOsVersion && (
                      <span className="text-slate-500 font-mono ml-auto">
                        FlowOS {item.flowOsVersion}
                      </span>
                    )}
                  </div>
                </button>
              );
            })}

            {filtered.length === 0 && (
              <div className="p-6 text-xs text-slate-500 text-center border border-dashed border-slate-800 rounded-xl">
                No workflows match this criteria.
              </div>
            )}
          </div>
        </section>

        {/* Right Column: Workflow Master-Detail Workspace */}
        <div className="space-y-4 min-w-0">
          {!selected ? (
            <div className="text-sm text-slate-400 border border-dashed border-slate-700 bg-slate-900 rounded-2xl p-12 text-center space-y-3">
              <FileText size={32} className="mx-auto text-slate-600" />
              <div className="font-bold text-white text-base">Select a Workflow Blueprint</div>
              <p className="text-xs text-slate-500 max-w-md mx-auto">
                Choose a workflow on the left to inspect its visual execution graph, bind Business Context data, configure AI Agents, and view live instances.
              </p>
              <button
                onClick={onCreate}
                className="mt-2 px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-xl text-xs font-semibold inline-flex items-center gap-1.5"
              >
                <Plus size={14} /> Create New Workflow
              </button>
            </div>
          ) : (
            <div className="space-y-4">
              {/* Selected Workflow Header Card */}
              <section className="rounded-2xl border border-blue-500/30 bg-slate-900 p-4 space-y-3">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-blue-500/20 text-blue-300 border border-blue-500/30 uppercase tracking-wide">
                        {WorkflowClassStatus[selected.status] ?? selected.status}
                      </span>
                      <span className="text-xs font-mono text-slate-400">
                        v{selected.version}
                      </span>
                      <span className="text-slate-600">·</span>
                      <span className="text-xs text-slate-500 font-mono">
                        UUID: {selected.id.substring(0, 10)}...
                      </span>
                      <button
                        onClick={() => handleCopy(selected.id, selected.id)}
                        className="text-slate-500 hover:text-white"
                        title="Copy Workflow UUID"
                      >
                        {copiedId === selected.id ? <Check size={11} className="text-emerald-400" /> : <Copy size={11} />}
                      </button>
                    </div>

                    <h2 className="text-2xl font-bold text-white mt-1">
                      {selected.name}
                    </h2>

                    <div className="flex items-center gap-2 mt-1 text-xs text-slate-400 flex-wrap">
                      <span>{steps.length} Steps</span>
                      <span>•</span>
                      <span>{relatedInstances.length} Live Instances</span>
                      {selected.flowOsVersion && (
                        <>
                          <span>•</span>
                          <span className="font-mono text-slate-500">FlowOS {selected.flowOsVersion}</span>
                        </>
                      )}
                    </div>
                  </div>

                  {/* Actions Toolbar */}
                  <div className="flex flex-wrap items-center gap-2">
                    <button
                      onClick={() => onView(selected.id)}
                      className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs font-medium text-slate-200 border border-slate-700 transition-colors"
                    >
                      Raw Blueprint
                    </button>

                    {selected.status === WorkflowClassStatus.Draft && (
                      <>
                        <button
                          onClick={() => onEdit(selected.id)}
                          className="px-3 py-1.5 rounded-lg bg-blue-600 hover:bg-blue-500 text-xs font-bold text-white flex items-center gap-1 shadow-sm transition-colors"
                        >
                          <Edit2 size={12} /> Edit Blueprint
                        </button>
                        <button
                          onClick={() => onPublish(selected.id)}
                          className="px-3 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-xs font-bold text-white flex items-center gap-1 shadow-sm transition-colors"
                        >
                          <CheckCircle size={12} /> Publish
                        </button>
                        <button
                          onClick={() => onDelete(selected.id)}
                          className="px-3 py-1.5 rounded-lg bg-rose-700 hover:bg-rose-600 text-xs font-semibold text-white flex items-center gap-1 transition-colors"
                        >
                          <Trash2 size={12} /> Delete
                        </button>
                      </>
                    )}

                    {(selected.status === WorkflowClassStatus.Published || selected.status === WorkflowClassStatus.Public) && (
                      <>
                        <button
                          onClick={() => onNewVersion(selected.id)}
                          className="px-3 py-1.5 rounded-lg bg-teal-700 hover:bg-teal-600 text-xs font-semibold text-white flex items-center gap-1 transition-colors"
                        >
                          <Plus size={12} /> New Version
                        </button>
                        <button
                          onClick={() => onSubmit(selected.id)}
                          className="px-3 py-1.5 rounded-lg bg-violet-600 hover:bg-violet-500 text-xs font-semibold text-white flex items-center gap-1 transition-colors"
                        >
                          <Send size={12} /> Submit
                        </button>
                        <button
                          onClick={() => onDeprecate(selected.id)}
                          className="px-3 py-1.5 rounded-lg bg-amber-700 hover:bg-amber-600 text-xs font-semibold text-white flex items-center gap-1 transition-colors"
                        >
                          <Archive size={12} /> Deprecate
                        </button>
                        <button
                          onClick={() => onAbandon(selected.id)}
                          className="px-3 py-1.5 rounded-lg bg-rose-800 hover:bg-rose-700 text-xs font-semibold text-white transition-colors"
                        >
                          Abandon
                        </button>
                      </>
                    )}

                    {selected.status === WorkflowClassStatus.Shared && (
                      <button
                        onClick={() => onWithdraw(selected.id)}
                        className="px-3 py-1.5 rounded-lg bg-amber-700 hover:bg-amber-600 text-xs font-semibold text-white transition-colors"
                      >
                        Withdraw
                      </button>
                    )}
                  </div>
                </div>

                {/* Cardinality Tabs */}
                <div className="flex bg-slate-950 rounded-xl p-1 border border-slate-800 text-xs gap-1">
                  <button
                    onClick={() => setDetailTab('graph')}
                    className={`flex-1 py-1.5 px-3 rounded-lg font-semibold flex items-center justify-center gap-1.5 transition-all ${
                      detailTab === 'graph' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
                    }`}
                  >
                    <Layers size={13} />
                    <span>Visual Graph & Steps</span>
                    <span className="font-mono text-[10px] opacity-75">({steps.length})</span>
                  </button>

                  <button
                    onClick={() => setDetailTab('business')}
                    className={`flex-1 py-1.5 px-3 rounded-lg font-semibold flex items-center justify-center gap-1.5 transition-all ${
                      detailTab === 'business' ? 'bg-amber-600 text-white shadow' : 'text-slate-400 hover:text-white'
                    }`}
                  >
                    <Link2 size={13} />
                    <span>Business Context (Data)</span>
                  </button>

                  <button
                    onClick={() => setDetailTab('ai')}
                    className={`flex-1 py-1.5 px-3 rounded-lg font-semibold flex items-center justify-center gap-1.5 transition-all ${
                      detailTab === 'ai' ? 'bg-violet-600 text-white shadow' : 'text-slate-400 hover:text-white'
                    }`}
                  >
                    <Brain size={13} />
                    <span>AI Context (Agents & Tools)</span>
                  </button>

                  <button
                    onClick={() => setDetailTab('instances')}
                    className={`flex-1 py-1.5 px-3 rounded-lg font-semibold flex items-center justify-center gap-1.5 transition-all ${
                      detailTab === 'instances' ? 'bg-emerald-600 text-white shadow' : 'text-slate-400 hover:text-white'
                    }`}
                  >
                    <Activity size={13} />
                    <span>Live Instances</span>
                    <span className="font-mono text-[10px] opacity-75">({relatedInstances.length})</span>
                  </button>
                </div>
              </section>

              {/* Tab 1: Visual Graph & Step Details */}
              {detailTab === 'graph' && (
                <div className="space-y-4">
                  {/* Steps Summary Pills */}
                  {steps.length > 0 && (
                    <div className="rounded-xl border border-slate-800 bg-slate-900 p-3.5 space-y-2">
                      <div className="text-[11px] font-bold text-slate-400 uppercase tracking-wider">
                        Workflow Steps ({steps.length})
                      </div>
                      <div className="flex flex-wrap gap-2">
                        {steps.map((st: any, idx: number) => {
                          const id = pick(st, 'stepId', 'StepId') || `step-${idx}`;
                          const role = pick(st, 'role', 'Role') || pick(st, 'allowedRoles', 'AllowedRoles')?.[0] || 'System';
                          const isAgent = String(role).toLowerCase().includes('agent') || Boolean(pick(st, 'agentProvider', 'AgentProvider'));
                          const isHuman = String(role).toLowerCase().includes('human') || String(role).toLowerCase().includes('officer') || String(role).toLowerCase().includes('user');

                          return (
                            <div
                              key={id}
                              className="px-2.5 py-1.5 rounded-lg bg-slate-950 border border-slate-800 flex items-center gap-2 text-xs"
                            >
                              <span className="font-mono font-bold text-white">{id}</span>
                              <span className={`text-[10px] px-1.5 py-0.2 rounded font-semibold ${
                                isAgent ? 'bg-purple-500/20 text-purple-300 border border-purple-500/30' :
                                isHuman ? 'bg-amber-500/20 text-amber-300 border border-amber-500/30' :
                                'bg-slate-800 text-slate-400 border border-slate-700'
                              }`}>
                                {isAgent ? 'Agent' : isHuman ? 'Human' : 'System'}: {role}
                              </span>
                            </div>
                          );
                        })}
                      </div>
                    </div>
                  )}

                  {/* Graph Visualizer */}
                  <div className="rounded-2xl border border-slate-700 bg-slate-900 overflow-hidden shadow-xl">
                    <WorkflowGraphVisualizer
                      definition={selected.definition}
                      initialView="both"
                    />
                  </div>
                </div>
              )}

              {/* Tab 2: Business Context Data Mapping */}
              {detailTab === 'business' && (
                <div className="rounded-2xl border border-amber-500/30 bg-slate-900 p-4">
                  <ContextBindingsView
                    compact
                    filterSourceWorkflowClassId={selected.id}
                    workflowClasses={publishedForBindings.length ? publishedForBindings : [selected]}
                    role="Tenant"
                    onSimulate={onSimulate}
                  />
                </div>
              )}

              {/* Tab 3: AI Context (Agents, Prompts, Tools) */}
              {detailTab === 'ai' && (
                <div className="rounded-2xl border border-violet-500/30 bg-slate-900 p-4">
                  <AiContextView
                    compact
                    tenantName={tenantName}
                    workflowClass={selected}
                  />
                </div>
              )}

              {/* Tab 4: Live Instances */}
              {detailTab === 'instances' && (
                <div className="rounded-2xl border border-emerald-500/30 bg-slate-900 p-4 space-y-3">
                  <div className="flex items-center justify-between pb-2 border-b border-slate-800">
                    <div className="text-xs font-bold text-white flex items-center gap-1.5">
                      <Activity size={14} className="text-emerald-400" />
                      Live Instances for {selected.name} ({relatedInstances.length})
                    </div>
                    <button
                      onClick={onLaunch}
                      className="px-3 py-1 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg text-xs font-semibold"
                    >
                      + Start Instance
                    </button>
                  </div>

                  {relatedInstances.length === 0 ? (
                    <div className="p-8 text-center text-xs text-slate-500 border border-dashed border-slate-800 rounded-xl">
                      No live instances currently executing this workflow blueprint.
                    </div>
                  ) : (
                    <div className="overflow-x-auto">
                      <table className="w-full text-left text-xs">
                        <thead>
                          <tr className="border-b border-slate-800 text-slate-400">
                            <th className="py-2 px-3">Instance UUID</th>
                            <th className="py-2 px-3">Step</th>
                            <th className="py-2 px-3">State</th>
                            <th className="py-2 px-3">Status</th>
                            <th className="py-2 px-3">Started</th>
                            <th className="py-2 px-3 text-right">Action</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-800/80">
                          {relatedInstances.map(inst => {
                            const id = inst.id || inst.workflowId || 'unknown';
                            const step = inst.currentStepId || inst.currentStep || 'Start';
                            const state = inst.currentState || 'Initial';
                            const status = typeof inst.status === 'number'
                              ? ['Running', 'Waiting', 'Completed', 'Failed'][inst.status]
                              : String(inst.status || 'Running');

                            return (
                              <tr key={id} className="hover:bg-slate-800/40">
                                <td className="py-2.5 px-3 font-mono text-white">
                                  {id.substring(0, 10)}...
                                </td>
                                <td className="py-2.5 px-3">
                                  <span className="px-2 py-0.5 rounded bg-blue-500/10 text-blue-300 font-mono text-[11px]">
                                    {step}
                                  </span>
                                </td>
                                <td className="py-2.5 px-3">
                                  <span className="px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-300 font-mono text-[11px]">
                                    {state}
                                  </span>
                                </td>
                                <td className="py-2.5 px-3">
                                  <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold border ${
                                    status === 'Completed' ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30' :
                                    status === 'Failed' ? 'bg-rose-500/20 text-rose-300 border-rose-500/30' :
                                    status === 'Waiting' ? 'bg-amber-500/20 text-amber-300 border-amber-500/30' :
                                    'bg-blue-500/20 text-blue-300 border-blue-500/30 animate-pulse'
                                  }`}>
                                    {status}
                                  </span>
                                </td>
                                <td className="py-2.5 px-3 text-slate-400">
                                  {new Date(inst.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                                </td>
                                <td className="py-2.5 px-3 text-right">
                                  {onInspectInstance && (
                                    <button
                                      onClick={() => onInspectInstance(id)}
                                      className="px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold flex items-center gap-1 ml-auto"
                                    >
                                      <History size={11} /> Inspect
                                    </button>
                                  )}
                                </td>
                              </tr>
                            );
                          })}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
