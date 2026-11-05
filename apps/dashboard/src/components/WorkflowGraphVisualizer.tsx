import React, { useState, useEffect, useRef, useCallback } from 'react';
import mermaid from 'mermaid';
import { enableNodeDragging, NodeDragging } from '../diagram/nodeDragging';
import { downloadDiagram, DiagramImageFormat, LegendEntry } from '../diagram/exportImage';
import { 
  Check,
  Download,
  ExternalLink,
  GitCommit, 
  Maximize2,
  Shield, 
  Layers,
  UserCheck,
  X,
  ZoomIn,
  ZoomOut
} from 'lucide-react';

export interface WorkflowGraphVisualizerProps {
  definition: any;
  currentStepId?: string;
  currentState?: string;
  instanceStatus?: string | number;
  completedSteps?: string[];
  initialView?: 'flowchart' | 'statemachine' | 'both';
}

const MIN_DIAGRAM_ZOOM = 0.5;
const MAX_DIAGRAM_ZOOM = 3;
const DIAGRAM_ZOOM_STEP = 0.25;

/** Stable localStorage key for a diagram's saved node arrangement. */
export function diagramLayoutKey(kind: string, ids: string[]): string {
  const joined = [...ids].map(String).sort().join('|');
  let hash = 5381;
  for (let i = 0; i < joined.length; i++) hash = ((hash << 5) + hash + joined.charCodeAt(i)) | 0;
  return `flowos.diagramLayout.v1.${kind}.${(hash >>> 0).toString(36)}`;
}

export function clampDiagramZoom(value: number) {
  const rounded = Math.round(value * 100) / 100;
  return Math.min(MAX_DIAGRAM_ZOOM, Math.max(MIN_DIAGRAM_ZOOM, rounded));
}

export function panForZoom(
  pan: { x: number; y: number },
  zoom: number,
  nextZoom: number,
  originX: number,
  originY: number
) {
  if (!zoom) return pan;
  const ratio = nextZoom / zoom;
  return {
    x: originX - (originX - pan.x) * ratio,
    y: originY - (originY - pan.y) * ratio
  };
}

/** Saves the diagram, as the user has arranged it, as a PNG or SVG file. */
const DiagramExportButtons: React.FC<{
  getSvg: () => SVGSVGElement | null;
  name: string;
  legend: LegendEntry[];
  large?: boolean;
}> = ({ getSvg, name, legend, large = false }) => {
  const [busy, setBusy] = useState<DiagramImageFormat | null>(null);
  const [error, setError] = useState<string | null>(null);

  const save = async (format: DiagramImageFormat) => {
    const svg = getSvg();
    if (!svg) {
      setError('There is no diagram to export yet.');
      return;
    }
    setBusy(format);
    setError(null);
    try {
      await downloadDiagram(svg, name, format, { legend });
    } catch (err: unknown) {
      setError((err as { message?: string } | null)?.message || 'The diagram could not be exported.');
    } finally {
      setBusy(null);
    }
  };

  const buttonClass = large
    ? 'inline-flex items-center gap-1 rounded-lg border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-xs text-slate-200 hover:border-slate-500 disabled:opacity-50'
    : 'inline-flex items-center gap-1 rounded border border-slate-700 bg-slate-900 px-2 py-0.5 text-[10px] font-semibold text-slate-200 hover:border-slate-500 disabled:opacity-50';

  return (
    <div className="flex items-center gap-1.5" onPointerDown={event => event.stopPropagation()}>
      <button type="button" className={buttonClass} disabled={busy !== null} onClick={() => void save('png')} aria-label={`Download ${name} as PNG image`} title="Download this diagram, as arranged, as a PNG image">
        <Download size={large ? 14 : 11} /> {busy === 'png' ? 'Saving…' : 'PNG'}
      </button>
      <button type="button" className={buttonClass} disabled={busy !== null} onClick={() => void save('svg')} aria-label={`Download ${name} as SVG image`} title="Download this diagram as a scalable SVG image">
        <Download size={large ? 14 : 11} /> {busy === 'svg' ? 'Saving…' : 'SVG'}
      </button>
      {error && <span role="alert" className="max-w-[16rem] text-[10px] text-rose-300">{error}</span>}
    </div>
  );
};

const MermaidViewer: React.FC<{ chart: string; unconstrained?: boolean; onRendered?: () => void; layoutKey?: string; exportName?: string; legendEntries?: LegendEntry[] }> = ({ chart, unconstrained = false, onRendered, layoutKey, exportName, legendEntries = [] }) => {
  const onRenderedRef = useRef(onRendered);
  onRenderedRef.current = onRendered;
  const containerRef = useRef<HTMLDivElement>(null);
  const draggingRef = useRef<NodeDragging | null>(null);
  const [movedNodes, setMovedNodes] = useState(0);

  useEffect(() => () => {
    draggingRef.current?.dispose();
    draggingRef.current = null;
  }, []);
  
  useEffect(() => {
    mermaid.initialize({
      startOnLoad: false,
      theme: 'dark',
      securityLevel: 'loose',
      fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace',
    });
    
    if (containerRef.current) {
      const id = 'mermaid-' + Math.random().toString(36).substring(7);
      mermaid.render(id, chart).then((result) => {
        if (containerRef.current) {
          containerRef.current.innerHTML = result.svg;
          if (unconstrained) {
            const svg = containerRef.current.querySelector('svg');
            if (svg) {
              svg.style.maxWidth = 'none';
              const box = svg.viewBox?.baseVal;
              if (box && box.width > 0 && box.height > 0) {
                svg.setAttribute('width', String(box.width));
                svg.setAttribute('height', String(box.height));
                svg.style.width = `${box.width}px`;
                svg.style.height = `${box.height}px`;
              } else {
                svg.style.width = 'auto';
                svg.style.height = 'auto';
              }
            }
          }
          draggingRef.current?.dispose();
          draggingRef.current = null;
          const renderedSvg = containerRef.current.querySelector('svg');
          if (renderedSvg) {
            draggingRef.current = enableNodeDragging(renderedSvg as SVGSVGElement, {
              storageKey: layoutKey,
              onChange: setMovedNodes
            });
          }
          onRenderedRef.current?.();
        }
      }).catch(err => {
        console.error('Mermaid render error', err);
        if (containerRef.current) {
          containerRef.current.innerHTML = `<div class="text-rose-400 text-xs p-4 border border-rose-500/30 bg-rose-500/10 rounded-lg">Error rendering graph: ${err.message}</div>`;
        }
      });
    }
  }, [chart, unconstrained, layoutKey]);

  return (
    <div className="relative w-full">
      <div
        ref={containerRef}
        data-diagram-canvas="true"
        className={`flex justify-center p-4 w-full ${unconstrained ? 'min-h-0' : 'overflow-auto min-h-[300px]'}`}
      />
      {exportName && (
        <div className="absolute right-2 top-2">
          <DiagramExportButtons
            getSvg={() => (containerRef.current?.querySelector('svg') as SVGSVGElement | null) ?? null}
            name={exportName}
            legend={legendEntries}
          />
        </div>
      )}
      <div className="absolute left-2 top-2 flex items-center gap-2 text-[10px] text-slate-500">
        <span>Drag nodes to rearrange</span>
        {movedNodes > 0 && (
          <button
            type="button"
            onPointerDown={event => event.stopPropagation()}
            onClick={() => draggingRef.current?.reset()}
            className="rounded border border-slate-700 bg-slate-900 px-2 py-0.5 font-semibold text-slate-200 hover:border-slate-500"
            title="Put every node back where the automatic layout placed it"
          >
            Reset layout
          </button>
        )}
      </div>
    </div>
  );
};

const ROLE_EVENT_COLORS = ['#38bdf8', '#f472b6', '#a78bfa', '#fbbf24', '#34d399', '#fb7185', '#22d3ee', '#e879f9'];
const SYSTEM_EVENT_COLOR = '#94a3b8';
/** ADR-003: distinct from both role colors and SYSTEM_EVENT_COLOR, so an agent/team-handled
 * step reads as neither "owned by a person" nor "plain automation". */
const AGENT_ACTOR_COLOR = '#f97316';

const isSystemRoleName = (role: string) => ['system', 'anyone', 'unassigned'].includes(role.trim().toLowerCase());

const mermaidEdgeText = (value: string) =>
  String(value || '').replace(/"/g, "'").replace(/\|/g, '/').replace(/\s+/g, ' ').trim();

export function eventRolesForDiagram(eventRoles: string[], stepRoles: string[] = []): string[] {
  const listed = (roles: string[]) => roles.map(role => role.trim()).filter(Boolean);
  const business = (roles: string[]) => listed(roles).filter(role => !isSystemRoleName(role));
  const fromEvent = business(eventRoles);
  if (fromEvent.length > 0) return fromEvent;
  const fromStep = business(stepRoles);
  if (fromStep.length > 0) return fromStep;
  if (listed(eventRoles).some(role => role.toLowerCase() === 'system') || listed(stepRoles).some(role => role.toLowerCase() === 'system')) {
    return ['System'];
  }
  return [];
}

export function roleEventStroke(role: string, roleOrder: string[]) {
  if (!role || isSystemRoleName(role)) return SYSTEM_EVENT_COLOR;
  const index = roleOrder.findIndex(existing => existing.toLowerCase() === role.toLowerCase());
  if (index < 0) return SYSTEM_EVENT_COLOR;
  return ROLE_EVENT_COLORS[index % ROLE_EVENT_COLORS.length];
}

const eventEdgeLabel = (eventId: string, roles: string[]) => {
  const business = roles.filter(role => !isSystemRoleName(role));
  const text = mermaidEdgeText(eventId);
  if (business.length > 0) return `${text} · ${business.map(mermaidEdgeText).join(', ')}`;
  if (roles.some(role => role.toLowerCase() === 'system')) return `${text} · System`;
  return text;
};

const RoleLegend: React.FC<{ entries: { role: string; stroke: string }[] }> = ({ entries }) => {
  if (entries.length === 0) return null;
  return (
    <div className="flex flex-wrap items-center gap-2" aria-label="Event roles">
      <span className="text-[10px] font-semibold uppercase tracking-wide text-slate-500">Event by role</span>
      {entries.map(entry => (
        <span key={entry.role} className="inline-flex items-center gap-1.5 rounded-full border border-slate-700 bg-slate-950 px-2 py-0.5 text-[11px] text-slate-200">
          <span className="h-2.5 w-2.5 rounded-full" style={{ background: entry.stroke }} />
          {entry.role}
        </span>
      ))}
    </div>
  );
};

const escapeHtml = (value: string) =>
  value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');

const openDiagramWindow = (title: string, svg: string, legend: { role: string; stroke: string }[] = []) => {
  const legendHtml = legend.map(entry =>
    `<span style="display:inline-flex;align-items:center;gap:6px;margin-right:10px;font-size:12px;color:#e2e8f0;"><span style="width:10px;height:10px;border-radius:999px;background:${entry.stroke};display:inline-block;"></span>${escapeHtml(entry.role)}</span>`
  ).join('');
  const popup = window.open('', '_blank', 'popup=yes,width=1400,height=900');
  if (!popup) {
    window.alert('Allow pop-ups to open this diagram in its own window.');
    return;
  }

  popup.document.open();
  popup.document.write(`<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <title>${escapeHtml(title)}</title>
  <style>
    html, body { margin: 0; height: 100%; background: #020617; color: #e2e8f0; font-family: ui-sans-serif, system-ui, sans-serif; }
    header { display: flex; justify-content: space-between; align-items: center; gap: 12px; padding: 12px 16px; background: #0f172a; border-bottom: 1px solid #1e293b; }
    button { background: #1e293b; color: #e2e8f0; border: 1px solid #334155; border-radius: 8px; padding: 6px 10px; cursor: pointer; }
    button:disabled { opacity: 0.4; cursor: default; }
    #viewport { position: relative; overflow: hidden; height: calc(100vh - 57px); cursor: grab; touch-action: none; }
    #viewport.panning { cursor: grabbing; }
    #canvas { position: absolute; top: 0; left: 0; transform-origin: 0 0; }
    svg { max-width: none !important; height: auto; }
  </style>
</head>
<body>
  <header>
    <div>
      <strong>${escapeHtml(title)}</strong>
      <div style="font-size: 11px; color: #94a3b8;">Scroll up to zoom in. Drag to pan. 50% to 300%.</div>
      <div>${legendHtml}</div>
    </div>
    <span>
      <button type="button" id="zoom-in">Zoom in</button>
      <button type="button" id="zoom-reset">100%</button>
      <button type="button" id="zoom-out">Zoom out</button>
    </span>
  </header>
  <main id="viewport"><div id="canvas">${svg}</div></main>
  <script>
    var scale = 1;
    var panX = 0;
    var panY = 0;
    var drag = null;
    var viewport = document.getElementById('viewport');
    var canvas = document.getElementById('canvas');
    var zoomOut = document.getElementById('zoom-out');
    var zoomIn = document.getElementById('zoom-in');
    var zoomReset = document.getElementById('zoom-reset');
    function clamp(value) {
      var rounded = Math.round(value * 100) / 100;
      if (rounded < 0.5) return 0.5;
      if (rounded > 3) return 3;
      return rounded;
    }
    function apply() {
      canvas.style.transform = 'translate(' + panX + 'px, ' + panY + 'px) scale(' + scale + ')';
      zoomReset.textContent = Math.round(scale * 100) + '%';
      zoomOut.disabled = scale <= 0.5;
      zoomIn.disabled = scale >= 3;
    }
    function zoomAt(next, originX, originY) {
      var clamped = clamp(next);
      var ratio = clamped / scale;
      panX = originX - (originX - panX) * ratio;
      panY = originY - (originY - panY) * ratio;
      scale = clamped;
      apply();
    }
    function center(next) {
      scale = clamp(next);
      panX = (viewport.clientWidth - canvas.offsetWidth * scale) / 2;
      panY = (viewport.clientHeight - canvas.offsetHeight * scale) / 2;
      apply();
    }
    zoomOut.addEventListener('click', function () {
      zoomAt(scale - 0.25, viewport.clientWidth / 2, viewport.clientHeight / 2);
    });
    zoomIn.addEventListener('click', function () {
      zoomAt(scale + 0.25, viewport.clientWidth / 2, viewport.clientHeight / 2);
    });
    zoomReset.addEventListener('click', function () { center(1); });
    viewport.addEventListener('wheel', function (event) {
      event.preventDefault();
      var rect = viewport.getBoundingClientRect();
      var step = event.deltaY < 0 ? 0.1 : -0.1;
      zoomAt(scale + step, event.clientX - rect.left, event.clientY - rect.top);
    }, { passive: false });
    viewport.addEventListener('pointerdown', function (event) {
      if (event.button !== 0) return;
      drag = { id: event.pointerId, x: event.clientX, y: event.clientY, panX: panX, panY: panY };
      viewport.classList.add('panning');
      if (viewport.setPointerCapture) viewport.setPointerCapture(event.pointerId);
    });
    viewport.addEventListener('pointermove', function (event) {
      if (!drag || drag.id !== event.pointerId) return;
      panX = drag.panX + (event.clientX - drag.x);
      panY = drag.panY + (event.clientY - drag.y);
      apply();
    });
    function endDrag(event) {
      if (!drag || drag.id !== event.pointerId) return;
      drag = null;
      viewport.classList.remove('panning');
    }
    viewport.addEventListener('pointerup', endDrag);
    viewport.addEventListener('pointercancel', endDrag);
    center(1);
  </script>
</body>
</html>`);
  popup.document.close();
};

const LargeDiagramWindow: React.FC<{
  title: string;
  subtitle: string;
  chart: string;
  layoutKey?: string;
  legendEntries: { role: string; stroke: string }[];
  onClose: () => void;
}> = ({ title, subtitle, chart, layoutKey, legendEntries, onClose }) => {
  const viewportRef = useRef<HTMLDivElement>(null);
  const canvasHostRef = useRef<HTMLDivElement>(null);
  const zoomRef = useRef(1);
  const panRef = useRef({ x: 0, y: 0 });
  const dragRef = useRef<{ pointerId: number; x: number; y: number; panX: number; panY: number } | null>(null);
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });

  const commit = useCallback((nextZoom: number, nextPan: { x: number; y: number }) => {
    zoomRef.current = nextZoom;
    panRef.current = nextPan;
    setZoom(nextZoom);
    setPan(nextPan);
  }, []);

  const centerDiagram = useCallback((nextZoom = 1) => {
    const viewport = viewportRef.current;
    const canvas = canvasHostRef.current;
    const clamped = clampDiagramZoom(nextZoom);
    if (!viewport || !canvas) {
      commit(clamped, { x: 0, y: 0 });
      return;
    }
    commit(clamped, {
      x: (viewport.clientWidth - canvas.offsetWidth * clamped) / 2,
      y: (viewport.clientHeight - canvas.offsetHeight * clamped) / 2
    });
  }, [commit]);

  const zoomAt = useCallback((nextZoom: number, originX: number, originY: number) => {
    const current = zoomRef.current;
    const clamped = clampDiagramZoom(nextZoom);
    commit(clamped, panForZoom(panRef.current, current, clamped, originX, originY));
  }, [commit]);

  const zoomBy = (delta: number) => {
    const viewport = viewportRef.current;
    zoomAt(zoomRef.current + delta, (viewport?.clientWidth ?? 0) / 2, (viewport?.clientHeight ?? 0) / 2);
  };

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    window.addEventListener('keydown', onKey);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', onKey);
    };
  }, [onClose]);

  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport) return;
    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      const rect = viewport.getBoundingClientRect();
      const step = event.deltaY < 0 ? 0.1 : -0.1;
      zoomAt(zoomRef.current + step, event.clientX - rect.left, event.clientY - rect.top);
    };
    viewport.addEventListener('wheel', onWheel, { passive: false });
    return () => viewport.removeEventListener('wheel', onWheel);
  }, [zoomAt]);

  const onPointerDown = (event: React.PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return;
    dragRef.current = {
      pointerId: event.pointerId,
      x: event.clientX,
      y: event.clientY,
      panX: panRef.current.x,
      panY: panRef.current.y
    };
    const target = event.currentTarget;
    if (typeof target.setPointerCapture === 'function') {
      try { target.setPointerCapture(event.pointerId); } catch { /* jsdom */ }
    }
  };

  const onPointerMove = (event: React.PointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    commit(zoomRef.current, {
      x: drag.panX + (event.clientX - drag.x),
      y: drag.panY + (event.clientY - drag.y)
    });
  };

  const endDrag = (event: React.PointerEvent<HTMLDivElement>) => {
    if (dragRef.current?.pointerId !== event.pointerId) return;
    dragRef.current = null;
  };

  return (
    <div className="fixed inset-0 z-[80] bg-slate-950/85 backdrop-blur-sm p-3 sm:p-5" role="dialog" aria-modal="true" aria-label={title}>
      <div className="flex h-full w-full flex-col overflow-hidden rounded-2xl border border-slate-700 bg-slate-950 shadow-2xl">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-800 bg-slate-900 px-4 py-3">
          <div>
            <h2 className="text-sm font-bold text-white">{title}</h2>
            <p className="text-[11px] text-slate-400">{subtitle}</p>
            <div className="mt-2"><RoleLegend entries={legendEntries} /></div>
          </div>
          <div className="flex flex-wrap items-center gap-1.5">
            <button type="button" onClick={() => zoomBy(DIAGRAM_ZOOM_STEP)} disabled={zoom >= MAX_DIAGRAM_ZOOM} className="inline-flex items-center gap-1 rounded-lg border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-xs text-slate-200 hover:border-slate-500 disabled:cursor-not-allowed disabled:opacity-40" aria-label="Zoom in" title="Zoom in. Makes the diagram larger.">
              <ZoomIn size={14} /> Zoom in
            </button>
            <button type="button" onClick={() => centerDiagram(1)} className="rounded-lg border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-xs font-mono text-slate-200 hover:border-slate-500" aria-label="Reset zoom">
              {Math.round(zoom * 100)}%
            </button>
            <button type="button" onClick={() => zoomBy(-DIAGRAM_ZOOM_STEP)} disabled={zoom <= MIN_DIAGRAM_ZOOM} className="inline-flex items-center gap-1 rounded-lg border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-xs text-slate-200 hover:border-slate-500 disabled:cursor-not-allowed disabled:opacity-40" aria-label="Zoom out" title="Zoom out. Makes the diagram smaller.">
              <ZoomOut size={14} /> Zoom out
            </button>
            <DiagramExportButtons
              large
              getSvg={() => (canvasHostRef.current?.querySelector('svg') as SVGSVGElement | null) ?? null}
              name={title}
              legend={legendEntries}
            />
            <button
              type="button"
              onClick={() => {
                const svg = canvasHostRef.current?.querySelector('svg')?.outerHTML;
                if (!svg) return;
                openDiagramWindow(title, svg, legendEntries);
              }}
              className="inline-flex items-center gap-1 rounded-lg border border-blue-500/40 bg-blue-600/20 px-2.5 py-1.5 text-xs font-semibold text-blue-100 hover:bg-blue-600/30"
            >
              <ExternalLink size={14} /> Own window
            </button>
            <button type="button" onClick={onClose} className="inline-flex items-center gap-1 rounded-lg border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-xs text-slate-200 hover:border-slate-500" aria-label="Close large diagram">
              <X size={14} /> Close
            </button>
          </div>
        </div>
        <div
          ref={viewportRef}
          data-testid="diagram-viewport"
          className="relative min-h-0 flex-1 overflow-hidden bg-slate-950 cursor-grab active:cursor-grabbing"
          style={{ touchAction: 'none' }}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={endDrag}
          onPointerCancel={endDrag}
        >
          <div
            ref={canvasHostRef}
            data-testid="diagram-canvas"
            className="absolute left-0 top-0"
            style={{ transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`, transformOrigin: '0 0' }}
          >
            <MermaidViewer chart={chart} unconstrained layoutKey={layoutKey} onRendered={() => centerDiagram(zoomRef.current)} />
          </div>
        </div>
      </div>
    </div>
  );
};

export const WorkflowGraphVisualizer: React.FC<WorkflowGraphVisualizerProps> = ({
  definition,
  currentStepId,
  currentState,
  instanceStatus,
  completedSteps = [],
  initialView = 'both'
}) => {
  const [activeTab, setActiveTab] = useState<'both' | 'flowchart' | 'statemachine'>(initialView);
  const [largeView, setLargeView] = useState<'flowchart' | 'statemachine' | null>(null);

  if (!definition) {
    return (
      <div className="p-8 text-center text-slate-500 text-xs italic bg-slate-950/40 rounded-xl border border-slate-800">
        No workflow definition data available to visualize.
      </div>
    );
  }

  // Safe case-insensitive helper to extract properties
  const getProp = (obj: any, ...keys: string[]) => {
    if (!obj) return undefined;
    for (const key of keys) {
      if (obj[key] !== undefined) return obj[key];
      const lower = key.toLowerCase();
      if (obj[lower] !== undefined) return obj[lower];
      const camel = key.charAt(0).toLowerCase() + key.slice(1);
      if (obj[camel] !== undefined) return obj[camel];
    }
    return undefined;
  };

  // Extract State Machine definition
  const smObj = getProp(definition, 'stateMachine', 'StateMachine') || {};
  const entityType = getProp(smObj, 'entityType', 'EntityType') || 'Entity';
  const initialState = getProp(smObj, 'initialState', 'InitialState') || '';
  const smStates: string[] = getProp(smObj, 'states', 'States') || [];
  const smTransitions: any[] = getProp(smObj, 'transitions', 'Transitions') || [];

  // Extract Workflow definition
  const wfObj = getProp(definition, 'workflow', 'Workflow') || definition;
  const startStepId = getProp(wfObj, 'startStepId', 'StartStepId') || 'Start';
  const rawSteps: any[] = getProp(wfObj, 'steps', 'Steps') || [];

  interface NormalizedStep {
    stepId: string;
    stepType: string;
    label?: string;
    roles: string[];
    nextSteps: Record<string, string>;
    conditions: Record<string, string>;
    branches?: string[];
    joinPolicy?: string;
    inboundSteps?: string[];
    sla?: { duration?: string; timeoutEvent?: string; escalationStepId?: string };
    totalHooks?: number;
    actor?: string;
    agentTeamId?: string;
    agentTeamSlotRole?: string;
    agentProvider?: string;
  }

  const steps: NormalizedStep[] = rawSteps.map(s => {
    const stepId = getProp(s, 'stepId', 'StepId') || '';
    const stepType = (getProp(s, 'stepType', 'StepType') || 'Command').toString();
    const label = getProp(s, 'label', 'Label') || stepId;
    const allowed = getProp(s, 'allowedRoles', 'AllowedRoles', 'requiredRoles', 'RequiredRoles', 'roles', 'Roles') || [];
    const roles: string[] = Array.isArray(allowed)
      ? allowed.map((r: any) => r.toString().trim()).filter(Boolean)
      : typeof allowed === 'string'
      ? allowed.split(',').map((r: string) => r.trim()).filter(Boolean)
      : allowed
      ? [allowed.toString()]
      : [];
    const nextSteps = getProp(s, 'nextSteps', 'NextSteps') || {};
    const conditions = getProp(s, 'conditions', 'Conditions') || {};
    
    const rawBranches = getProp(s, 'branches', 'Branches') || [];
    const branches: string[] = Array.isArray(rawBranches)
      ? rawBranches
      : typeof rawBranches === 'string'
      ? rawBranches.split(',').map((b: string) => b.trim()).filter(Boolean)
      : [];
    const joinPolicy = getProp(s, 'joinPolicy', 'JoinPolicy') || 'WaitAll';
    const rawInbound = getProp(s, 'inboundSteps', 'InboundSteps') || [];
    const inboundSteps: string[] = Array.isArray(rawInbound)
      ? rawInbound
      : typeof rawInbound === 'string'
      ? rawInbound.split(',').map((b: string) => b.trim()).filter(Boolean)
      : [];

    const slaRaw = getProp(s, 'sla', 'Sla', 'SLA');
    const sla = slaRaw ? {
      duration: getProp(slaRaw, 'duration', 'Duration'),
      timeoutEvent: getProp(slaRaw, 'timeoutEvent', 'TimeoutEvent'),
      escalationStepId: getProp(slaRaw, 'escalationStepId', 'EscalationStepId')
    } : undefined;

    const onEntryList = getProp(s, 'onEntry', 'OnEntry') || [];
    const onExitList = getProp(s, 'onExit', 'OnExit') || [];
    const totalHooks = (Array.isArray(onEntryList) ? onEntryList.length : 0) +
                       (Array.isArray(onExitList) ? onExitList.length : 0);

    const actor = (getProp(s, 'actor', 'Actor') || '').toString();
    const agentTeamIdRaw = getProp(s, 'agentTeamId', 'AgentTeamId');
    const agentTeamId = agentTeamIdRaw ? String(agentTeamIdRaw) : undefined;
    const agentTeamSlotRole = getProp(s, 'agentTeamSlotRole', 'AgentTeamSlotRole') || undefined;
    const agentProvider = getProp(s, 'agentProvider', 'AgentProvider') || undefined;

    return {
      stepId,
      stepType,
      label,
      roles,
      nextSteps,
      conditions,
      branches,
      joinPolicy,
      inboundSteps,
      sla,
      totalHooks,
      actor,
      agentTeamId,
      agentTeamSlotRole,
      agentProvider
    };
  });

  // ADR-003: a step is agent-handled either because its actor is explicitly Agent/Either,
  // or because its stepType is AgentTask (AgentTaskScheduling treats AgentTask as agent-handled
  // regardless of the actor field - see WorkflowClassValidator.ValidateTeamActorBinding).
  const isAgentHandledStep = (step: NormalizedStep) => {
    const actor = (step.actor || '').toLowerCase();
    return actor === 'agent' || actor === 'either' || step.stepType.toLowerCase() === 'agenttask';
  };

  // A team actor only dispatches when stepType is AgentTask (an agentTeamId set on any other
  // step type compiles but is never read - WF-AGENT-008), so only show the team badge there.
  const agentActorBadge = (step: NormalizedStep): { text: string; isTeam: boolean } | null => {
    if (!isAgentHandledStep(step)) return null;
    const isAgentTask = step.stepType.toLowerCase() === 'agenttask';
    if (isAgentTask && step.agentTeamId) {
      const shortTeamId = step.agentTeamId.split('-')[0];
      return {
        text: step.agentTeamSlotRole ? `Team · ${step.agentTeamSlotRole}` : `Team · ${shortTeamId}`,
        isTeam: true
      };
    }
    return { text: step.agentProvider ? `Agent · ${step.agentProvider}` : 'Agent', isTeam: false };
  };

  // Sort / Organize steps topologically starting with startStepId
  const orderedSteps: NormalizedStep[] = [];
  const visited = new Set<string>();
  
  const addStepRecursive = (stepId: string) => {
    if (!stepId || visited.has(stepId)) return;
    visited.add(stepId);
    const found = steps.find(s => s.stepId.toLowerCase() === stepId.toLowerCase());
    if (found) {
      orderedSteps.push(found);
      // Follow next step targets
      Object.values(found.nextSteps).forEach(target => addStepRecursive(target as string));
      Object.values(found.conditions).forEach(target => addStepRecursive(target as string));
      if (found.branches && Array.isArray(found.branches)) {
        found.branches.forEach((b: string) => addStepRecursive(b));
      }
      if (found.sla?.escalationStepId) {
        addStepRecursive(found.sla.escalationStepId);
      }
    }
  };

  if (startStepId) {
    addStepRecursive(startStepId);
  }
  steps.forEach(s => {
    if (!visited.has(s.stepId)) {
      orderedSteps.push(s);
      visited.add(s.stepId);
    }
  });

  const activeStep = steps.find(s => s.stepId.toLowerCase() === (currentStepId || '').toLowerCase());
  const workflowFinished =
    String(instanceStatus || '').toLowerCase() === 'completed' ||
    ['end', 'none'].includes(String(currentStepId || '').trim().toLowerCase());

  const safeId = (id: string) => (id || '').toString().replace(/[^a-zA-Z0-9_]/g, '_');
  const catalogEvents: any[] = getProp(definition, 'events', 'Events') || [];
  const roleList = (value: unknown): string[] => {
    if (Array.isArray(value)) return value.map(role => String(role).trim()).filter(Boolean);
    if (typeof value === 'string') return value.split(',').map(role => role.trim()).filter(Boolean);
    return [];
  };
  const rolesForEvent = (eventId: string, stepRoles: string[] = []) => {
    const found = catalogEvents.find((event: any) => {
      const id = String(getProp(event, 'eventId', 'EventId', 'name', 'Name') || '').trim().toLowerCase();
      return id === eventId.trim().toLowerCase();
    });
    const declared = roleList(getProp(found, 'allowedRoles', 'AllowedRoles', 'requiredRoles', 'RequiredRoles', 'roles', 'Roles'));
    return eventRolesForDiagram(declared, stepRoles);
  };
  const stepById = (stepId: string) => steps.find(step => step.stepId.toLowerCase() === String(stepId || '').trim().toLowerCase());
  const roleOrder: string[] = [];
  let diagramUsesSystem = Boolean(initialState);
  const rememberRoles = (roles: string[]) => {
    if (!roles.some(role => role && !isSystemRoleName(role))) diagramUsesSystem = true;
    roles.forEach(role => {
      if (!role || isSystemRoleName(role)) return;
      if (roleOrder.some(existing => existing.toLowerCase() === role.toLowerCase())) return;
      roleOrder.push(role);
    });
  };
  orderedSteps.forEach(step => {
    Object.keys(step.nextSteps).forEach(eventId => rememberRoles(rolesForEvent(eventId, step.roles)));
    Object.keys(step.conditions).forEach(() => rememberRoles(eventRolesForDiagram([], step.roles)));
    (step.branches || []).forEach(target => rememberRoles(eventRolesForDiagram([], stepById(target)?.roles || [])));
    if (step.sla?.escalationStepId) rememberRoles(rolesForEvent(step.sla.timeoutEvent || 'TIMEOUT', step.roles));
  });
  smTransitions.forEach(transition => {
    const eventId = String(getProp(transition, 'eventId', 'EventId', 'eventName', 'EventName', 'triggerEvent', 'TriggerEvent') || '');
    if (eventId) rememberRoles(rolesForEvent(eventId));
  });
  const strokeFor = (roles: string[]) => roleEventStroke(roles.find(role => !isSystemRoleName(role)) || 'System', roleOrder);
  const styledEdges = (lines: { statement: string; roles: string[] }[]) => {
    const body = lines.map(line => line.statement).join('');
    const styles = lines.map((line, index) =>
      `linkStyle ${index} stroke:${strokeFor(line.roles)},stroke-width:2px,color:${strokeFor(line.roles)}`
    );
    return `${body}${styles.length > 0 ? `\n${styles.join('\n')}\n` : ''}`;
  };
  // The arrangement a user made is remembered per diagram kind and per set of steps/states.
  const layoutKey = (kind: 'flowchart' | 'statemachine') =>
    diagramLayoutKey(kind, kind === 'flowchart'
      ? [startStepId, ...steps.map(step => step.stepId)]
      : [entityType, ...smStates]);
  const hasAgentHandledSteps = steps.some(isAgentHandledStep);
  const legendEntries = [
    ...roleOrder.map(role => ({ role, stroke: roleEventStroke(role, roleOrder) })),
    ...(diagramUsesSystem ? [{ role: 'System', stroke: SYSTEM_EVENT_COLOR }] : []),
    ...(hasAgentHandledSteps ? [{ role: '🤖 Agent / 🤝 Team', stroke: AGENT_ACTOR_COLOR }] : [])
  ];

  const buildWorkflowMermaid = () => {
    let chart = 'flowchart TD\n';
    chart += 'classDef current fill:#f59e0b,stroke:#b45309,color:#000,stroke-width:3px\n';
    chart += 'classDef completed fill:#0f766e,stroke:#047857,color:#fff\n';
    chart += 'classDef reached fill:#059669,stroke:#34d399,color:#fff,stroke-width:3px\n';
    chart += 'classDef pending fill:#1e293b,stroke:#334155,color:#cbd5e1\n\n';

    const activeTokens = (currentStepId || '')
      .split(',')
      .map(s => s.trim().toLowerCase())
      .filter(Boolean);

    orderedSteps.forEach(step => {
      const isCurrent = activeTokens.includes(step.stepId.toLowerCase());
      const isCompleted = completedSteps.some(cs => cs.toLowerCase() === step.stepId.toLowerCase());
      
      const className = isCurrent ? ':::current' : isCompleted ? ':::completed' : ':::pending';
      let shapeStart = '([';
      let shapeEnd = '])';
      
      const t = step.stepType.toLowerCase();
      if (t.includes('fork')) {
        shapeStart = '{{'; shapeEnd = '}}';
      } else if (t.includes('join')) {
        shapeStart = '[/'; shapeEnd = '/]';
      } else if (t.includes('decision') || t.includes('choice')) {
        shapeStart = '{'; shapeEnd = '}';
      } else if (t.includes('timer') || t.includes('delay')) {
        shapeStart = '(('; shapeEnd = '))';
      } else if (t.includes('end')) {
        shapeStart = '((('; shapeEnd = ')))';
      }
      
      const cleanLabel = (step.label || step.stepId).replace(/["{[\]}]/g, '');
      let displayLabel = cleanLabel;
      if (t.includes('fork')) {
        displayLabel = `🔀 FORK: ${cleanLabel}`;
      } else if (t.includes('join')) {
        displayLabel = `🔗 JOIN (${step.joinPolicy || 'WaitAll'}): ${cleanLabel}`;
      }
      if (step.roles.length > 0) {
        displayLabel += `<br/>(Role: ${step.roles.join(', ')})`;
      }
      if (step.sla?.duration) {
         displayLabel += `<br/>⏱️ SLA: ${step.sla.duration}`;
      }
      if (step.totalHooks && step.totalHooks > 0) {
         displayLabel += `<br/>⚡ ${step.totalHooks} Hook${step.totalHooks > 1 ? 's' : ''}`;
      }
      const badge = agentActorBadge(step);
      if (badge) {
        displayLabel += `<br/>${badge.isTeam ? '🤝' : '🤖'} ${badge.text}`;
      }
      chart += `  ${safeId(step.stepId)}${shapeStart}"${displayLabel}"${shapeEnd}${className}\n`;
      const outgoingRoles = Object.keys(step.nextSteps).flatMap(eventId => rolesForEvent(eventId, step.roles));
      const owner = outgoingRoles.find(role => !isSystemRoleName(role))
        || step.roles.find(role => !isSystemRoleName(role));
      if (!isCurrent && !isCompleted && owner) {
        chart += `  style ${safeId(step.stepId)} stroke:${roleEventStroke(owner, roleOrder)},stroke-width:2px\n`;
      } else if (!isCurrent && !isCompleted && badge) {
        // Agent/team-handled steps with no human owner role get their own stroke so they read
        // as distinct from plain System automation (Command/SystemTask with no actor at all).
        chart += `  style ${safeId(step.stepId)} stroke:${AGENT_ACTOR_COLOR},stroke-width:2px,stroke-dasharray:3 2\n`;
      }
    });

    const knownIds = new Set(orderedSteps.map(step => step.stepId.toLowerCase()));
    const endTargets = new Set<string>();
    const noteEnd = (target: string) => {
      const key = String(target || '').trim();
      if (!key) return;
      const normalized = key.toLowerCase();
      if ((normalized === 'end' || normalized === 'none') && !knownIds.has(normalized)) {
        endTargets.add(key);
      }
    };
    orderedSteps.forEach(step => {
      Object.values(step.nextSteps).forEach(target => noteEnd(String(target)));
      Object.values(step.conditions).forEach(target => noteEnd(String(target)));
    });
    const activeIsEnd = activeTokens.some(token => token === 'end' || token === 'none');
    if (activeIsEnd) endTargets.add('END');
    const workflowFinished =
      String(instanceStatus || '').toLowerCase() === 'completed' || activeIsEnd;
    endTargets.forEach(target => {
      chart += `  ${safeId(target)}(((END)))${workflowFinished ? ':::reached' : ':::pending'}\n`;
    });

    chart += '\n';
    const edges: { statement: string; roles: string[] }[] = [];
    orderedSteps.forEach(step => {
      if (step.branches && Array.isArray(step.branches) && step.branches.length > 0) {
        step.branches.forEach((branchTarget, bIdx) => {
          const roles = eventRolesForDiagram([], stepById(branchTarget)?.roles || []);
          edges.push({
            statement: `  ${safeId(step.stepId)} ==>|"${eventEdgeLabel(`Branch ${bIdx + 1}`, roles)}"| ${safeId(branchTarget)}\n`,
            roles
          });
        });
      }
      Object.entries(step.nextSteps).forEach(([evt, target]) => {
        const roles = rolesForEvent(evt, step.roles);
        edges.push({
          statement: `  ${safeId(step.stepId)} -->|"${eventEdgeLabel(evt, roles)}"| ${safeId(target)}\n`,
          roles
        });
      });
      Object.entries(step.conditions).forEach(([expr, target]) => {
        const roles = eventRolesForDiagram([], step.roles);
        edges.push({
          statement: `  ${safeId(step.stepId)} -->|"${eventEdgeLabel(expr, roles)}"| ${safeId(target)}\n`,
          roles
        });
      });
      if (step.sla?.escalationStepId) {
        const timeoutEvt = step.sla.timeoutEvent || 'TIMEOUT';
        const roles = rolesForEvent(timeoutEvt, step.roles);
        edges.push({
          statement: `  ${safeId(step.stepId)} -.->|"${eventEdgeLabel(timeoutEvt, roles)}"| ${safeId(step.sla.escalationStepId)}\n`,
          roles
        });
      }
    });

    return chart + styledEdges(edges);
  };

  const buildStateMachineMermaid = () => {
    let chart = 'flowchart LR\n';
    chart += 'classDef current fill:#10b981,stroke:#047857,color:#111827,stroke-width:3px\n';
    chart += 'classDef normal fill:#1e293b,stroke:#334155,color:#e2e8f0\n\n';

    const stateNames = [...smStates];
    if (initialState && !stateNames.some(state => state.toLowerCase() === initialState.toLowerCase())) {
      stateNames.unshift(initialState);
    }
    stateNames.forEach(state => {
      const isCurrent = currentState && currentState.toLowerCase() === state.toLowerCase();
      chart += `  ${safeId(state)}(["${mermaidEdgeText(state)}"])${isCurrent ? ':::current' : ':::normal'}\n`;
    });

    const edges: { statement: string; roles: string[] }[] = [];
    if (initialState) {
      chart += '  __start__((start)):::normal\n';
      edges.push({ statement: `  __start__ --> ${safeId(initialState)}\n`, roles: [] });
    }
    smTransitions.forEach((transition) => {
      const from = String(getProp(transition, 'fromState', 'FromState') || '*');
      const to = String(getProp(transition, 'toState', 'ToState') || '');
      const eventId = String(getProp(transition, 'eventId', 'EventId', 'eventName', 'EventName', 'triggerEvent', 'TriggerEvent') || 'Event');
      if (!to) return;
      const roles = rolesForEvent(eventId);
      const sources = from === '*' ? stateNames.filter(state => state !== to) : [from];
      sources.forEach(source => {
        edges.push({
          statement: `  ${safeId(source)} -->|"${eventEdgeLabel(eventId, roles)}"| ${safeId(to)}\n`,
          roles
        });
      });
    });
    if (instanceStatus && String(instanceStatus).toLowerCase() === 'completed' && currentState) {
      chart += '  __done__((done)):::current\n';
      edges.push({ statement: `  ${safeId(currentState)} --> __done__\n`, roles: [] });
    }

    return chart + styledEdges(edges);
  };

  return (
    <div className="space-y-4">
      {/* Top View Selector Bar */}
      <div className="flex flex-wrap items-center justify-between gap-3 bg-slate-900/90 p-2.5 rounded-xl border border-slate-800">
        <div className="flex items-center gap-2">
          <Layers size={16} className="text-blue-400" />
          <span className="text-xs font-bold text-white tracking-wide">Visual Blueprint (Mermaid Graph)</span>
        </div>

        <div className="flex items-center gap-1 bg-slate-950 p-1 rounded-lg border border-slate-800 text-xs">
          <button
            onClick={() => setActiveTab('both')}
            className={`px-2.5 py-1 rounded transition-all font-medium ${
              activeTab === 'both' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
            }`}
          >
            Combined View
          </button>
          <button
            onClick={() => setActiveTab('flowchart')}
            className={`px-2.5 py-1 rounded transition-all font-medium ${
              activeTab === 'flowchart' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
            }`}
          >
            Workflow Steps
          </button>
          <button
            onClick={() => setActiveTab('statemachine')}
            className={`px-2.5 py-1 rounded transition-all font-medium ${
              activeTab === 'statemachine' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
            }`}
          >
            State Machine
          </button>
        </div>
      </div>

      <RoleLegend entries={legendEntries} />

      {/* Live Instance Status Banner */}
      {currentStepId && (
        <div className={`rounded-xl p-4 shadow-sm flex flex-col md:flex-row md:items-center justify-between gap-3 border ${
          workflowFinished
            ? 'bg-emerald-950/50 border-emerald-500/40'
            : 'bg-slate-900/90 border-slate-700/80'
        }`}>
          <div className="flex items-center gap-3">
            {workflowFinished ? (
              <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-emerald-500 text-slate-950">
                <Check size={14} />
              </span>
            ) : (
              <span className="relative flex h-3 w-3">
                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-amber-400 opacity-75"></span>
                <span className="relative inline-flex rounded-full h-3 w-3 bg-amber-500"></span>
              </span>
            )}
            <div>
              <div className="text-xs font-semibold text-white flex flex-wrap items-center gap-2">
                <span>{workflowFinished ? 'Workflow reached' : `Active Execution Step${(currentStepId || '').includes(',') ? 's' : ''}:`}</span>
                {(currentStepId || '').split(',').map((tok, tIdx) => (
                  <span key={tIdx} className={`px-2 py-0.5 rounded font-mono text-xs border ${
                    workflowFinished
                      ? 'bg-emerald-500/20 text-emerald-200 border-emerald-400/40'
                      : 'bg-amber-500/20 text-amber-300 border-amber-500/30'
                  }`}>
                    {tok.trim()}
                  </span>
                ))}
                {currentState && (
                  <>
                    <span className="text-slate-500">•</span>
                    <span>Legal State:</span>
                    <span className="px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-300 font-mono text-xs border border-emerald-500/30">
                      {currentState}
                    </span>
                  </>
                )}
                {!workflowFinished && (
                  <>
                    <span className="text-slate-500">•</span>
                    <span>Active Role:</span>
                    <span className={`px-2 py-0.5 rounded font-mono text-xs border flex items-center gap-1 font-semibold ${
                      !activeStep?.roles?.length && activeStep && agentActorBadge(activeStep)
                        ? 'bg-orange-500/20 text-orange-300 border-orange-500/30'
                        : 'bg-indigo-500/20 text-indigo-300 border-indigo-500/30'
                    }`}>
                      {activeStep?.roles && activeStep.roles.length > 0 ? (
                        <>
                          <UserCheck size={12} className="text-indigo-400" />
                          {activeStep.roles.join(', ')}
                        </>
                      ) : activeStep && agentActorBadge(activeStep) ? (
                        <>
                          <span>{agentActorBadge(activeStep)!.isTeam ? '🤝' : '🤖'}</span>
                          {agentActorBadge(activeStep)!.text}
                        </>
                      ) : (
                        <>
                          <UserCheck size={12} className="text-indigo-400" />
                          {activeStep?.stepType.toLowerCase().includes('human') ? 'Unassigned' : 'System'}
                        </>
                      )}
                    </span>
                  </>
                )}
              </div>
              <p className="text-[11px] text-slate-400 mt-0.5">
                {workflowFinished
                  ? `Both kernels are finished. Workflow step is END${currentState ? ` and the state machine is ${currentState}` : ''}.`
                  : activeStep?.roles && activeStep.roles.length > 0
                  ? `Pausing in engine: Awaiting Human Task sign-off by role [${activeStep.roles.join(', ')}]`
                  : activeStep && agentActorBadge(activeStep)
                  ? `Pausing in engine: Awaiting ${agentActorBadge(activeStep)!.isTeam ? 'an orchestrated AgentTeam' : 'an AI agent'} to propose the next event (${agentActorBadge(activeStep)!.text})`
                  : activeStep?.stepType.toLowerCase().includes('human')
                  ? `Pausing in engine: Awaiting Human Task sign-off (Any Role)`
                  : `Automated engine action executing under System role for step [${currentStepId}]`}
              </p>
            </div>
          </div>
        </div>
      )}

      {/* Main Graph Grid */}
      <div className={`grid gap-4 ${activeTab === 'both' ? 'grid-cols-1 lg:grid-cols-2' : 'grid-cols-1'}`}>
        
        {/* SECTION 1: WORKFLOW STEPS FLOWCHART */}
        {(activeTab === 'both' || activeTab === 'flowchart') && (
          <div className="bg-slate-950/70 border border-slate-800 rounded-xl p-4 flex flex-col items-center">
            <div className="w-full flex items-center justify-between gap-2 border-b border-slate-800 pb-2 mb-2">
              <div className="flex items-center gap-2">
                <GitCommit size={15} className="text-blue-400" />
                <h3 className="text-xs font-bold text-white uppercase tracking-wider">
                  Workflow Orchestration Steps
                </h3>
              </div>
              <div className="flex items-center gap-2">
                <span className="text-[11px] text-slate-400">
                  Start: <code className="text-blue-300 font-mono">{startStepId}</code>
                </span>
                {orderedSteps.length > 0 && (
                  <button
                    type="button"
                    onClick={() => setLargeView('flowchart')}
                    className="inline-flex items-center gap-1 rounded-lg border border-slate-700 bg-slate-900 px-2 py-1 text-[11px] font-semibold text-slate-200 hover:border-blue-400 hover:text-white"
                    aria-label="View workflow diagram large"
                  >
                    <Maximize2 size={12} /> View large
                  </button>
                )}
              </div>
            </div>
            {orderedSteps.length > 0 ? (
              <MermaidViewer chart={buildWorkflowMermaid()} layoutKey={layoutKey('flowchart')} exportName="Workflow Orchestration Steps" legendEntries={legendEntries} />
            ) : (
              <div className="py-8 text-slate-500 text-xs">No steps defined</div>
            )}
          </div>
        )}

        {/* SECTION 2: STATE MACHINE GRAPH */}
        {(activeTab === 'both' || activeTab === 'statemachine') && (
          <div className="bg-slate-950/70 border border-slate-800 rounded-xl p-4 flex flex-col items-center">
            <div className="w-full flex items-center justify-between gap-2 border-b border-slate-800 pb-2 mb-2">
              <div className="flex items-center gap-2">
                <Shield size={15} className="text-emerald-400" />
                <h3 className="text-xs font-bold text-white uppercase tracking-wider">
                  State Machine Lifecycle
                </h3>
              </div>
              <div className="flex items-center gap-2">
                <span className="text-[11px] text-slate-400">
                  Entity: <code className="text-emerald-300 font-mono">{entityType}</code>
                </span>
                {smStates.length > 0 && (
                  <button
                    type="button"
                    onClick={() => setLargeView('statemachine')}
                    className="inline-flex items-center gap-1 rounded-lg border border-slate-700 bg-slate-900 px-2 py-1 text-[11px] font-semibold text-slate-200 hover:border-emerald-400 hover:text-white"
                    aria-label="View state diagram large"
                  >
                    <Maximize2 size={12} /> View large
                  </button>
                )}
              </div>
            </div>
            {smStates.length > 0 ? (
              <MermaidViewer chart={buildStateMachineMermaid()} layoutKey={layoutKey('statemachine')} exportName="State Machine Lifecycle" legendEntries={legendEntries} />
            ) : (
              <div className="py-8 text-slate-500 text-xs">No states defined</div>
            )}
          </div>
        )}

      </div>

      {largeView === 'flowchart' && (
        <LargeDiagramWindow
          title="Workflow Orchestration Steps"
          subtitle={`Start step ${startStepId}. Scroll and zoom this diagram on its own.`}
          chart={buildWorkflowMermaid()}
          layoutKey={layoutKey('flowchart')}
          legendEntries={legendEntries}
          onClose={() => setLargeView(null)}
        />
      )}
      {largeView === 'statemachine' && (
        <LargeDiagramWindow
          title="State Machine Lifecycle"
          subtitle={`${entityType}. Scroll and zoom this diagram on its own.`}
          chart={buildStateMachineMermaid()}
          layoutKey={layoutKey('statemachine')}
          legendEntries={legendEntries}
          onClose={() => setLargeView(null)}
        />
      )}
    </div>
  );
};
