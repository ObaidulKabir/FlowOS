import React, { useEffect, useMemo, useState } from 'react';
import { FlaskConical, Link2, Sparkles } from 'lucide-react';
import { WorkflowClass } from '../types';
import { DraftSimulator } from './DraftSimulator';
import { ContextSimulationStudio } from './ContextSimulationStudio';
import { DEMO_FALLBACKS, DEMO_NAMES } from '../lib/demoWorkflows';

interface Props {
  blueprints: WorkflowClass[];
  initialBindingId?: string;
  initialRevision?: 'draft' | 'active';
  preferContextMode?: boolean;
}

type Mode = 'visual' | 'context';

export const DemoVisualSimulator: React.FC<Props> = ({
  blueprints,
  initialBindingId,
  initialRevision,
  preferContextMode = false
}) => {
  const [mode, setMode] = useState<Mode>(preferContextMode ? 'context' : 'visual');

  const catalog = useMemo(() => {
    const preferred = DEMO_NAMES
      .map(name => blueprints.find(bp => bp.name === name))
      .filter((bp): bp is WorkflowClass => Boolean(bp));

    const preferredIds = new Set(preferred.map(bp => bp.id));
    const extras = blueprints.filter(bp => bp.definition && !preferredIds.has(bp.id));

    const items = [...preferred, ...extras].map(bp => ({
      id: bp.id,
      name: bp.name,
      version: bp.version,
      definition: bp.definition,
      source: 'catalog' as const
    }));

    DEMO_NAMES.forEach(name => {
      if (items.some(item => item.name === name)) return;
      const fallback = DEMO_FALLBACKS[name];
      if (!fallback) return;
      items.push({
        id: fallback.id,
        name: fallback.name,
        version: fallback.version,
        definition: fallback.definition,
        source: 'catalog'
      });
    });

    items.sort((a, b) => {
      const ai = DEMO_NAMES.indexOf(a.name);
      const bi = DEMO_NAMES.indexOf(b.name);
      return (ai === -1 ? 99 : ai) - (bi === -1 ? 99 : bi);
    });

    return items;
  }, [blueprints]);

  const [selectedId, setSelectedId] = useState(catalog[0]?.id || '');

  useEffect(() => {
    if (!catalog.some(item => item.id === selectedId)) {
      setSelectedId(catalog[0]?.id || '');
    }
  }, [catalog, selectedId]);

  const selected = catalog.find(item => item.id === selectedId) || catalog[0];

  return (
    <div className="space-y-4">
      <div className="rounded-2xl border border-emerald-500/30 bg-gradient-to-r from-emerald-950/40 via-slate-900 to-teal-950/30 p-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2 text-emerald-300 font-bold text-sm">
            <Sparkles size={16} />
            Visual Demo Simulator
          </div>
          <p className="text-xs text-slate-400 mt-1 max-w-3xl">
            Walk the dual-kernel graph, fire events, and test roles <strong className="text-emerald-300">in-memory</strong>.
            No workflow instance is started and nothing is written to production runtime.
          </p>
        </div>
        <div className="flex bg-slate-950 rounded-xl p-1 border border-slate-800 text-xs font-semibold">
          <button
            type="button"
            onClick={() => setMode('visual')}
            className={`px-3 py-1.5 rounded-lg flex items-center gap-1.5 transition-all ${
              mode === 'visual' ? 'bg-emerald-600 text-white shadow' : 'text-slate-400 hover:text-white'
            }`}
          >
            <FlaskConical size={13} /> Visual Sandbox
          </button>
          <button
            type="button"
            onClick={() => setMode('context')}
            className={`px-3 py-1.5 rounded-lg flex items-center gap-1.5 transition-all ${
              mode === 'context' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
            }`}
          >
            <Link2 size={13} /> Context Studio
          </button>
        </div>
      </div>

      {mode === 'context' ? (
        <ContextSimulationStudio
          role="Tenant"
          initialBindingId={initialBindingId}
          initialRevision={initialRevision}
        />
      ) : (
        <div className="space-y-3">
          <div className="flex flex-wrap gap-2">
            {catalog.slice(0, 8).map(item => (
              <button
                key={item.id}
                type="button"
                onClick={() => setSelectedId(item.id)}
                className={`px-3 py-1.5 rounded-xl border text-xs font-semibold transition-all ${
                  selected?.id === item.id
                    ? 'bg-emerald-600/20 border-emerald-500 text-emerald-200'
                    : 'bg-slate-950 border-slate-800 text-slate-400 hover:border-slate-600 hover:text-white'
                }`}
              >
                {item.name}
                <span className="ml-1.5 text-[10px] opacity-70">v{item.version}</span>
              </button>
            ))}
          </div>

          {selected?.definition ? (
            <div className="rounded-2xl border border-slate-700 bg-slate-900/60 overflow-hidden min-h-[520px]">
              <DraftSimulator key={selected.id} definition={selected.definition} />
            </div>
          ) : (
            <div className="rounded-2xl border border-dashed border-slate-700 p-10 text-center text-sm text-slate-400">
              No workflow definition available for simulation.
            </div>
          )}
        </div>
      )}
    </div>
  );
};
