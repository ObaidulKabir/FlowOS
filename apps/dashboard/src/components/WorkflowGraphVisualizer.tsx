import React, { useState, useEffect, useRef } from 'react';
import mermaid from 'mermaid';
import { 
  Check,
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

const MermaidViewer: React.FC<{ chart: string; unconstrained?: boolean }> = ({ chart, unconstrained = false }) => {
  const containerRef = useRef<HTMLDivElement>(null);
  
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
              svg.style.height = 'auto';
            }
          }
        }
      }).catch(err => {
        console.error('Mermaid render error', err);
        if (containerRef.current) {
          containerRef.current.innerHTML = `<div class="text-rose-400 text-xs p-4 border border-rose-500/30 bg-rose-500/10 rounded-lg">Error rendering graph: ${err.message}</div>`;
        }
      });
    }
  }, [chart, unconstrained]);

  return (
    <div
      ref={containerRef}
      data-diagram-canvas="true"
      className={`flex justify-center p-4 w-full ${unconstrained ? 'min-h-0' : 'overflow-auto min-h-[300px]'}`}
    />
  );
};

const ROLE_EVENT_COLORS = ['#38bdf8', '#f472b6', '#a78bfa', '#fbbf24', '#34d399', '#fb7185', '#22d3ee', '#e879f9'];
const SYSTEM_EVENT_COLOR = '#94a3b8';

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
    html, body { margin: 0; background: #020617; color: #e2e8f0; font-family: ui-sans-serif, system-ui, sans-serif; }
    header { position: sticky; top: 0; z-index: 2; display: flex; justify-content: space-between; align-items: center; gap: 12px; padding: 12px 16px; background: #0f172a; border-bottom: 1px solid #1e293b; }
    button { background: #1e293b; color: #e2e8f0; border: 1px solid #334155; border-radius: 8px; padding: 6px 10px; cursor: pointer; }
    main { overflow: auto; height: calc(100vh - 53px); }
    .canvas { transform-origin: top center; padding: 32px; display: flex; justify-content: center; }
    svg { max-width: none !important; height: auto; }
  </style>
</head>
<body>
  <header>
    <div>
      <strong>${escapeHtml(title)}</strong>
      <div>${legend.map(entry => `<span style="display:inline-flex;align-items:center;gap:6px;margin-right:10px;font-size:12px;color:#e2e8f0;"><span style="width:10px;height:10px;border-radius:999px;background:${entry.stroke};display:inline-block;"></span>${escapeHtml(entry.role)}</span>`).join('')}</div>
    </div>
    <span>
      <button type="button" onclick="zoom(0.8)">Smaller</button>
      <button type="button" onclick="setZoom(1)">100%</button>
      <button type="button" onclick="zoom(1.25)">Larger</button>
    </span>
  </header>
  <main><div id="canvas" class="canvas">${svg}</div></main>
  <script>
    let scale = 1;
    function setZoom(next) {
      scale = Math.min(4, Math.max(0.25, next));
      document.getElementById('canvas').style.zoom = String(scale);
    }
    function zoom(factor) { setZoom(scale * factor); }
  </script>
</body>
</html>`);
  popup.document.close();
};

const LargeDiagramWindow: React.FC<{
  title: string;
  subtitle: string;
  chart: string;
  legendEntries: { role: string; stroke: string }[];
  onClose: () => void;
}> = ({ title, subtitle, chart, legendEntries, onClose }) => {
  const canvasHostRef = useRef<HTMLDivElement>(null);
  const [zoom, setZoom] = useState(1);

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

  const changeZoom = (next: number) => setZoom(Math.min(3, Math.max(0.4, Number(next.toFixed(2)))));

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
            <button type="button" onClick={() => changeZoom(zoom / 1.25)} className="inline-flex items-center gap-1 rounded-lg border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-xs text-slate-200 hover:border-slate-500" aria-label="Zoom out">
              <ZoomOut size={14} /> Smaller
            </button>
            <button type="button" onClick={() => changeZoom(1)} className="rounded-lg border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-xs font-mono text-slate-200 hover:border-slate-500">
              {Math.round(zoom * 100)}%
            </button>
            <button type="button" onClick={() => changeZoom(zoom * 1.25)} className="inline-flex items-center gap-1 rounded-lg border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-xs text-slate-200 hover:border-slate-500" aria-label="Zoom in">
              <ZoomIn size={14} /> Larger
            </button>
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
        <div className="min-h-0 flex-1 overflow-auto bg-slate-950">
          <div ref={canvasHostRef} style={{ zoom }}>
            <MermaidViewer chart={chart} unconstrained />
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
      totalHooks
    };
  });

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
  const legendEntries = [
    ...roleOrder.map(role => ({ role, stroke: roleEventStroke(role, roleOrder) })),
    ...(diagramUsesSystem ? [{ role: 'System', stroke: SYSTEM_EVENT_COLOR }] : [])
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
      chart += `  ${safeId(step.stepId)}${shapeStart}"${displayLabel}"${shapeEnd}${className}\n`;
      const outgoingRoles = Object.keys(step.nextSteps).flatMap(eventId => rolesForEvent(eventId, step.roles));
      const owner = outgoingRoles.find(role => !isSystemRoleName(role))
        || step.roles.find(role => !isSystemRoleName(role));
      if (!isCurrent && !isCompleted && owner) {
        chart += `  style ${safeId(step.stepId)} stroke:${roleEventStroke(owner, roleOrder)},stroke-width:2px\n`;
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
                    <span className="px-2 py-0.5 rounded bg-indigo-500/20 text-indigo-300 font-mono text-xs border border-indigo-500/30 flex items-center gap-1 font-semibold">
                      <UserCheck size={12} className="text-indigo-400" />
                      {activeStep?.roles && activeStep.roles.length > 0 
                        ? activeStep.roles.join(', ') 
                        : (activeStep?.stepType.toLowerCase().includes('human') ? 'Unassigned' : 'System')}
                    </span>
                  </>
                )}
              </div>
              <p className="text-[11px] text-slate-400 mt-0.5">
                {workflowFinished
                  ? `Both kernels are finished. Workflow step is END${currentState ? ` and the state machine is ${currentState}` : ''}.`
                  : activeStep?.roles && activeStep.roles.length > 0 
                  ? `Pausing in engine: Awaiting Human Task sign-off by role [${activeStep.roles.join(', ')}]`
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
              <MermaidViewer chart={buildWorkflowMermaid()} />
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
              <MermaidViewer chart={buildStateMachineMermaid()} />
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
          legendEntries={legendEntries}
          onClose={() => setLargeView(null)}
        />
      )}
      {largeView === 'statemachine' && (
        <LargeDiagramWindow
          title="State Machine Lifecycle"
          subtitle={`${entityType}. Scroll and zoom this diagram on its own.`}
          chart={buildStateMachineMermaid()}
          legendEntries={legendEntries}
          onClose={() => setLargeView(null)}
        />
      )}
    </div>
  );
};
