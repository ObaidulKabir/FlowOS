import React, { useState, useEffect, useRef } from 'react';
import mermaid from 'mermaid';
import { 
  GitCommit, 
  Shield, 
  Layers
} from 'lucide-react';

export interface WorkflowGraphVisualizerProps {
  definition: any;
  currentStepId?: string;
  currentState?: string;
  instanceStatus?: string | number;
  completedSteps?: string[];
  initialView?: 'flowchart' | 'statemachine' | 'both';
}

const MermaidViewer: React.FC<{ chart: string }> = ({ chart }) => {
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
        }
      }).catch(err => {
        console.error('Mermaid render error', err);
        if (containerRef.current) {
          containerRef.current.innerHTML = `<div class="text-rose-400 text-xs p-4 border border-rose-500/30 bg-rose-500/10 rounded-lg">Error rendering graph: ${err.message}</div>`;
        }
      });
    }
  }, [chart]);

  return <div ref={containerRef} className="flex justify-center p-4 overflow-auto min-h-[300px] w-full" />;
};

export const WorkflowGraphVisualizer: React.FC<WorkflowGraphVisualizerProps> = ({
  definition,
  currentStepId,
  currentState,
  completedSteps = [],
  initialView = 'both'
}) => {
  const [activeTab, setActiveTab] = useState<'both' | 'flowchart' | 'statemachine'>(initialView);

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

  // Normalize steps
  interface NormalizedStep {
    stepId: string;
    stepType: string;
    label?: string;
    roles: string[];
    nextSteps: Record<string, string>;
    conditions: Record<string, string>;
  }

  const steps: NormalizedStep[] = rawSteps.map(s => {
    const stepId = getProp(s, 'stepId', 'StepId') || '';
    const stepType = (getProp(s, 'stepType', 'StepType') || 'Command').toString();
    const label = getProp(s, 'label', 'Label') || stepId;
    const allowed = getProp(s, 'allowedRoles', 'AllowedRoles', 'requiredRoles', 'RequiredRoles') || [];
    const roles: string[] = Array.isArray(allowed) ? allowed : allowed ? [allowed.toString()] : [];
    const nextSteps = getProp(s, 'nextSteps', 'NextSteps') || {};
    const conditions = getProp(s, 'conditions', 'Conditions') || {};

    return {
      stepId,
      stepType,
      label,
      roles,
      nextSteps,
      conditions
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

  const buildWorkflowMermaid = () => {
    let chart = 'flowchart TD\n';
    chart += 'classDef current fill:#f59e0b,stroke:#b45309,color:#000,stroke-width:3px\n';
    chart += 'classDef completed fill:#0f766e,stroke:#047857,color:#fff\n';
    chart += 'classDef pending fill:#1e293b,stroke:#334155,color:#cbd5e1\n\n';

    orderedSteps.forEach(step => {
      const isCurrent = currentStepId && currentStepId.toLowerCase() === step.stepId.toLowerCase();
      const isCompleted = completedSteps.some(cs => cs.toLowerCase() === step.stepId.toLowerCase());
      
      const className = isCurrent ? ':::current' : isCompleted ? ':::completed' : ':::pending';
      let shapeStart = '([';
      let shapeEnd = '])';
      
      const t = step.stepType.toLowerCase();
      if (t.includes('decision') || t.includes('choice')) {
        shapeStart = '{'; shapeEnd = '}';
      } else if (t.includes('timer') || t.includes('delay')) {
        shapeStart = '(('; shapeEnd = '))';
      } else if (t.includes('end')) {
        shapeStart = '((('; shapeEnd = ')))';
      }
      
      const cleanLabel = (step.label || step.stepId).replace(/["{[\]}]/g, '');
      const displayLabel = step.roles.length > 0 ? `${cleanLabel}<br/>(Role: ${step.roles.join(', ')})` : cleanLabel;
      chart += `  ${step.stepId}${shapeStart}"${displayLabel}"${shapeEnd}${className}\n`;
    });

    chart += '\n';

    orderedSteps.forEach(step => {
      Object.entries(step.nextSteps).forEach(([evt, target]) => {
        chart += `  ${step.stepId} -->|"${evt}"| ${target}\n`;
      });
      Object.entries(step.conditions).forEach(([expr, target]) => {
        chart += `  ${step.stepId} -->|"${expr}"| ${target}\n`;
      });
    });

    return chart;
  };

  const buildStateMachineMermaid = () => {
    let chart = 'stateDiagram-v2\n';
    chart += 'classDef current fill:#10b981,stroke:#047857,color:#000,font-weight:bold\n';
    chart += 'classDef normal fill:#1e293b,stroke:#334155,color:#cbd5e1\n\n';

    if (initialState) {
      chart += `  [*] --> ${initialState}\n`;
    }

    smTransitions.forEach((t) => {
      const from = getProp(t, 'fromState', 'FromState') || '*';
      const to = getProp(t, 'toState', 'ToState') || '';
      const evt = getProp(t, 'eventId', 'EventId', 'eventName', 'EventName') || 'Event';
      
      if (from === '*') {
        smStates.forEach(s => {
           if (s !== to) {
              chart += `  ${s} --> ${to} : ${evt}\n`;
           }
        });
      } else {
        chart += `  ${from} --> ${to} : ${evt}\n`;
      }
    });

    chart += '\n';
    smStates.forEach(st => {
       const isCurrent = currentState && currentState.toLowerCase() === st.toLowerCase();
       if (isCurrent) {
          chart += `  class ${st} current\n`;
       } else {
          chart += `  class ${st} normal\n`;
       }
    });

    return chart;
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

      {/* Live Instance Status Banner */}
      {currentStepId && (
        <div className="bg-slate-900/90 border border-slate-700/80 rounded-xl p-4 shadow-sm flex flex-col md:flex-row md:items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <span className="relative flex h-3 w-3">
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-amber-400 opacity-75"></span>
              <span className="relative inline-flex rounded-full h-3 w-3 bg-amber-500"></span>
            </span>
            <div>
              <div className="text-xs font-semibold text-white flex items-center gap-2">
                Active Execution Step:
                <span className="px-2 py-0.5 rounded bg-amber-500/20 text-amber-300 font-mono text-xs border border-amber-500/30">
                  {currentStepId}
                </span>
                {currentState && (
                  <>
                    <span className="text-slate-500">•</span>
                    <span>Legal State:</span>
                    <span className="px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-300 font-mono text-xs border border-emerald-500/30">
                      {currentState}
                    </span>
                  </>
                )}
              </div>
              <p className="text-[11px] text-slate-400 mt-0.5">
                {activeStep?.roles && activeStep.roles.length > 0 
                  ? `Pausing in engine: Awaiting Human Task sign-off by role [${activeStep.roles.join(', ')}]`
                  : `Instance currently advancing through step [${currentStepId}]`}
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
            <div className="w-full flex items-center justify-between border-b border-slate-800 pb-2 mb-2">
              <div className="flex items-center gap-2">
                <GitCommit size={15} className="text-blue-400" />
                <h3 className="text-xs font-bold text-white uppercase tracking-wider">
                  Workflow Orchestration Steps
                </h3>
              </div>
              <span className="text-[11px] text-slate-400">
                Start: <code className="text-blue-300 font-mono">{startStepId}</code>
              </span>
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
            <div className="w-full flex items-center justify-between border-b border-slate-800 pb-2 mb-2">
              <div className="flex items-center gap-2">
                <Shield size={15} className="text-emerald-400" />
                <h3 className="text-xs font-bold text-white uppercase tracking-wider">
                  State Machine Lifecycle
                </h3>
              </div>
              <span className="text-[11px] text-slate-400">
                Entity: <code className="text-emerald-300 font-mono">{entityType}</code>
              </span>
            </div>
            {smStates.length > 0 ? (
              <MermaidViewer chart={buildStateMachineMermaid()} />
            ) : (
              <div className="py-8 text-slate-500 text-xs">No states defined</div>
            )}
          </div>
        )}

      </div>
    </div>
  );
};
