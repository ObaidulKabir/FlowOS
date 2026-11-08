import React, { Component, ErrorInfo, ReactNode, useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { FlaskConical, Link2, Maximize2, Minimize2, Sparkles } from 'lucide-react';
import { WorkflowClass } from '../types';
import { DraftSimulator } from './DraftSimulator';
import { ContextSimulationStudio } from './ContextSimulationStudio';
import { DEMO_FALLBACKS, DEMO_NAMES, ensureAmountRoutedExpenseApproval } from '../lib/demoWorkflows';

class SimulatorErrorBoundary extends Component<
  { children: ReactNode },
  { error: string | null }
> {
  state = { error: null as string | null };

  static getDerivedStateFromError(error: Error) {
    return { error: error.message || 'Simulator failed to render' };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Visual Demo Simulator crashed', error, info.componentStack);
  }

  render() {
    if (this.state.error) {
      return (
        <div className="rounded-2xl border border-rose-500/40 bg-rose-950/30 p-6 text-sm text-rose-200">
          <div className="font-bold mb-1">Visual Demo Simulator could not open</div>
          <p className="text-xs text-rose-300/80">{this.state.error}</p>
        </div>
      );
    }
    return this.props.children;
  }
}

interface Props {
  blueprints: WorkflowClass[];
  initialBindingId?: string;
  initialRevision?: 'draft' | 'active';
  preferContextMode?: boolean;
}

type Mode = 'visual' | 'context';

/** Portal-rendered fullscreen overlay */
const FullscreenOverlay: React.FC<{ onExit: () => void; children: ReactNode }> = ({ onExit, children }) => {
  // Lock body scroll while fullscreen
  useEffect(() => {
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => { document.body.style.overflow = prev; };
  }, []);

  // Escape key exits fullscreen
  useEffect(() => {
    const handler = (e: KeyboardEvent) => { if (e.key === 'Escape') onExit(); };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onExit]);

  return createPortal(
    <div className="fixed inset-0 z-[9999] bg-slate-950 flex flex-col overflow-hidden">
      {children}
    </div>,
    document.body
  );
};

export const DemoVisualSimulator: React.FC<Props> = ({
  blueprints,
  initialBindingId,
  initialRevision,
  preferContextMode = false
}) => {
  const [mode, setMode] = useState<Mode>(preferContextMode ? 'context' : 'visual');
  const [isFullscreen, setIsFullscreen] = useState(false);
  const toggleFullscreen = useCallback(() => setIsFullscreen(f => !f), []);

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
      definition: bp.name === 'ExpenseApprovalV2'
        ? ensureAmountRoutedExpenseApproval(structuredClone(bp.definition))
        : bp.definition,
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
      // Prioritize tenant's own workflows (-1) over system defaults
      return (ai === -1 ? -1 : ai) - (bi === -1 ? -1 : bi);
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

  /** Header bar — shared between normal and fullscreen modes */
  const header = (
    <div className={`rounded-2xl border border-emerald-500/30 bg-gradient-to-r from-emerald-950/40 via-slate-900 to-teal-950/30 p-4 flex flex-wrap items-start justify-between gap-3 ${isFullscreen ? 'rounded-none border-x-0 border-t-0' : ''}`}>
      <div>
        <div className="flex items-center gap-2 text-emerald-300 font-bold text-sm">
          <Sparkles size={16} />
          Visual Demo Simulator
          {isFullscreen && <span className="ml-1 text-xs text-emerald-400/70 font-normal">(fullscreen — press Esc to exit)</span>}
        </div>
        <p className="text-xs text-slate-400 mt-1 max-w-3xl">
          Walk the dual-kernel graph, fire events, and test roles <strong className="text-emerald-300">in-memory</strong>.
          No workflow instance is started and nothing is written to production runtime.
        </p>
      </div>
      <div className="flex items-center gap-2">
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
        {/* Fullscreen toggle */}
        <button
          type="button"
          onClick={toggleFullscreen}
          title={isFullscreen ? 'Exit fullscreen (Esc)' : 'Enter fullscreen'}
          className="p-2 rounded-xl border border-slate-700 bg-slate-950 text-slate-400 hover:text-white hover:border-emerald-500 transition-all"
        >
          {isFullscreen ? <Minimize2 size={15} /> : <Maximize2 size={15} />}
        </button>
      </div>
    </div>
  );

  /** Body — shared between normal and fullscreen modes */
  const body = (
    <>
      {mode === 'context' ? (
        <ContextSimulationStudio
          role="Tenant"
          initialBindingId={initialBindingId}
          initialRevision={initialRevision}
        />
      ) : (
        <div className={`space-y-3 ${isFullscreen ? 'flex flex-col flex-1 overflow-hidden' : ''}`}>
          <div className="flex items-center gap-2 bg-slate-900 p-2 rounded-xl border border-slate-800">
            <span className="text-xs font-semibold text-slate-400 ml-2">Simulate Blueprint:</span>
            <select
              value={selected?.id || ''}
              onChange={e => setSelectedId(e.target.value)}
              className="bg-slate-950 border border-slate-700 text-white text-sm rounded-lg px-3 py-1.5 min-w-[300px] outline-none focus:border-emerald-500"
            >
              {catalog.map(item => (
                <option key={item.id} value={item.id}>
                  {item.name} v{item.version} {DEMO_NAMES.indexOf(item.name) !== -1 ? '(System Default)' : '(Your Workflow)'}
                </option>
              ))}
            </select>
          </div>

          {selected?.definition ? (
            <div className={`rounded-2xl border border-slate-700 bg-slate-900/60 overflow-hidden ${isFullscreen ? 'flex-1' : 'min-h-[520px]'}`}>
              <SimulatorErrorBoundary>
                <DraftSimulator key={selected.id} definition={selected.definition} />
              </SimulatorErrorBoundary>
            </div>
          ) : (
            <div className="rounded-2xl border border-dashed border-slate-700 p-10 text-center text-sm text-slate-400">
              No workflow definition available for simulation.
            </div>
          )}
        </div>
      )}
    </>
  );

  if (isFullscreen) {
    return (
      <>
        {/* Keep normal placeholder in document flow so scroll position is preserved */}
        <div className="space-y-4 opacity-0 pointer-events-none select-none" aria-hidden="true">
          {header}
        </div>
        <FullscreenOverlay onExit={toggleFullscreen}>
          <div className="flex flex-col h-full overflow-hidden p-4 gap-4">
            {header}
            <div className="flex-1 overflow-hidden">
              {body}
            </div>
          </div>
        </FullscreenOverlay>
      </>
    );
  }

  return (
    <div className="space-y-4">
      {header}
      {body}
    </div>
  );
};
