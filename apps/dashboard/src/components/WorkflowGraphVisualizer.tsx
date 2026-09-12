import React, { useState } from 'react';
import { 
  GitCommit, 
  ArrowDown, 
  UserCheck, 
  Terminal, 
  Clock, 
  GitFork, 
  CircleDot, 
  Shield, 
  Layers,
  Check,
  ChevronRight
} from 'lucide-react';

export interface WorkflowGraphVisualizerProps {
  definition: any;
  currentStepId?: string;
  currentState?: string;
  instanceStatus?: string | number;
  completedSteps?: string[];
  initialView?: 'flowchart' | 'statemachine' | 'both';
}

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
      Object.values(found.nextSteps).forEach(target => addStepRecursive(target));
      Object.values(found.conditions).forEach(target => addStepRecursive(target));
    }
  };

  if (startStepId) {
    addStepRecursive(startStepId);
  }
  // Add any unvisited remaining steps
  steps.forEach(s => {
    if (!visited.has(s.stepId)) {
      orderedSteps.push(s);
      visited.add(s.stepId);
    }
  });

  // Find active step
  const activeStep = steps.find(s => s.stepId.toLowerCase() === (currentStepId || '').toLowerCase());

  // Step type badges and icons
  const getStepTypeInfo = (type: string) => {
    const t = type.toLowerCase();
    if (t.includes('human') || t.includes('user')) {
      return {
        label: 'Human Task',
        icon: <UserCheck size={13} className="text-amber-400" />,
        badgeClass: 'bg-amber-500/10 text-amber-400 border-amber-500/30'
      };
    }
    if (t.includes('decision') || t.includes('choice')) {
      return {
        label: 'Decision',
        icon: <GitFork size={13} className="text-purple-400" />,
        badgeClass: 'bg-purple-500/10 text-purple-400 border-purple-500/30'
      };
    }
    if (t.includes('timer')) {
      return {
        label: 'Timer / SLA',
        icon: <Clock size={13} className="text-sky-400" />,
        badgeClass: 'bg-sky-500/10 text-sky-400 border-sky-500/30'
      };
    }
    if (t.includes('end')) {
      return {
        label: 'Terminal End',
        icon: <CircleDot size={13} className="text-emerald-400" />,
        badgeClass: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/30'
      };
    }
    return {
      label: 'Command Action',
      icon: <Terminal size={13} className="text-blue-400" />,
      badgeClass: 'bg-blue-500/10 text-blue-400 border-blue-500/30'
    };
  };

  return (
    <div className="space-y-4">
      {/* Top View Selector Bar */}
      <div className="flex flex-wrap items-center justify-between gap-3 bg-slate-900/90 p-2.5 rounded-xl border border-slate-800">
        <div className="flex items-center gap-2">
          <Layers size={16} className="text-blue-400" />
          <span className="text-xs font-bold text-white tracking-wide">Visual Blueprint & Runtime Graph</span>
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
            Workflow Steps ({steps.length})
          </button>
          <button
            onClick={() => setActiveTab('statemachine')}
            className={`px-2.5 py-1 rounded transition-all font-medium ${
              activeTab === 'statemachine' ? 'bg-blue-600 text-white shadow' : 'text-slate-400 hover:text-white'
            }`}
          >
            State Machine ({smStates.length} States)
          </button>
        </div>
      </div>

      {/* Live Instance Status Banner (If running instance telemetry provided) */}
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

          {/* Next allowable events */}
          {activeStep && Object.keys(activeStep.nextSteps).length > 0 && (
            <div className="text-right text-[11px]">
              <span className="text-slate-400 block mb-1">Permitted Next Events:</span>
              <div className="flex flex-wrap gap-1 justify-end">
                {Object.entries(activeStep.nextSteps).map(([evt, target]) => (
                  <span key={evt} className="px-2 py-0.5 rounded bg-blue-500/10 text-blue-300 border border-blue-500/30 font-mono text-[10px]" title={`Advances to: ${target}`}>
                    {evt} &rarr; {target}
                  </span>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Main Graph Grid */}
      <div className={`grid gap-4 ${activeTab === 'both' ? 'grid-cols-1 lg:grid-cols-12' : 'grid-cols-1'}`}>
        
        {/* SECTION 1: WORKFLOW STEPS FLOWCHART */}
        {(activeTab === 'both' || activeTab === 'flowchart') && (
          <div className={`${activeTab === 'both' ? 'lg:col-span-7' : 'w-full'} bg-slate-950/70 border border-slate-800 rounded-xl p-4 space-y-4`}>
            <div className="flex items-center justify-between border-b border-slate-800 pb-2">
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

            {/* Steps Container */}
            <div className="space-y-3 py-1">
              {orderedSteps.map((step, idx) => {
                const typeInfo = getStepTypeInfo(step.stepType);
                const isCurrent = currentStepId && currentStepId.toLowerCase() === step.stepId.toLowerCase();
                const isCompleted = completedSteps.some(cs => cs.toLowerCase() === step.stepId.toLowerCase());

                let borderStyle = 'border-slate-800 bg-slate-900/60';
                if (isCurrent) {
                  borderStyle = 'border-amber-500 ring-2 ring-amber-500/20 bg-amber-950/20 shadow-lg';
                } else if (isCompleted) {
                  borderStyle = 'border-emerald-500/50 bg-emerald-950/10';
                }

                return (
                  <div key={step.stepId} className="relative group">
                    {/* Node Card */}
                    <div className={`border rounded-xl p-3.5 transition-all ${borderStyle}`}>
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex items-center gap-2">
                          <span className={`p-1 rounded-md border text-[10px] font-bold uppercase flex items-center gap-1 ${typeInfo.badgeClass}`}>
                            {typeInfo.icon}
                            {typeInfo.label}
                          </span>
                          <h4 className="font-bold text-sm text-white">{step.stepId}</h4>
                        </div>

                        {/* Status Marker in Live Mode */}
                        {currentStepId && (
                          <div>
                            {isCurrent ? (
                              <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-amber-500 text-slate-950 animate-pulse">
                                CURRENT ACTIVE
                              </span>
                            ) : isCompleted ? (
                              <span className="px-2 py-0.5 rounded text-[10px] font-medium bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 flex items-center gap-1">
                                <Check size={10} /> Completed
                              </span>
                            ) : (
                              <span className="px-2 py-0.5 rounded text-[10px] bg-slate-800 text-slate-500">
                                Pending
                              </span>
                            )}
                          </div>
                        )}
                      </div>

                      {/* Required Roles / Details */}
                      {step.roles.length > 0 && (
                        <div className="mt-2 flex items-center gap-1.5 text-[11px] text-amber-300/90 font-medium">
                          <Shield size={12} className="text-amber-400" />
                          <span>Authorized Roles:</span>
                          <span className="px-1.5 py-0.5 rounded bg-amber-500/20 font-mono text-[10px]">
                            {step.roles.join(', ')}
                          </span>
                        </div>
                      )}

                      {/* Outgoing Transitions */}
                      {Object.keys(step.nextSteps).length > 0 && (
                        <div className="mt-2.5 pt-2 border-t border-slate-800/80 text-[11px] space-y-1">
                          <div className="text-slate-400 text-[10px] font-semibold uppercase tracking-wider">
                            Transitions on Event:
                          </div>
                          <div className="flex flex-wrap gap-1.5">
                            {Object.entries(step.nextSteps).map(([evt, target]) => (
                              <div key={evt} className="flex items-center gap-1 bg-slate-950 px-2 py-0.5 rounded border border-slate-800 text-[11px]">
                                <span className="text-blue-400 font-mono font-medium">{evt}</span>
                                <ChevronRight size={11} className="text-slate-600" />
                                <span className="text-white font-semibold">{target}</span>
                              </div>
                            ))}
                          </div>
                        </div>
                      )}

                      {/* Conditional Decisions */}
                      {Object.keys(step.conditions).length > 0 && (
                        <div className="mt-2.5 pt-2 border-t border-slate-800/80 text-[11px] space-y-1">
                          <div className="text-purple-300 text-[10px] font-semibold uppercase tracking-wider">
                            Decision Rules:
                          </div>
                          <div className="space-y-1 font-mono text-[10px]">
                            {Object.entries(step.conditions).map(([expr, target]) => (
                              <div key={expr} className="bg-slate-950 p-1 rounded border border-slate-800 flex justify-between">
                                <span className="text-purple-300">{expr}</span>
                                <span className="text-white font-bold">&rarr; {target}</span>
                              </div>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>

                    {/* Step Connector Arrow */}
                    {idx < orderedSteps.length - 1 && (
                      <div className="flex justify-center py-1">
                        <ArrowDown size={16} className="text-slate-700" />
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        )}

        {/* SECTION 2: STATE MACHINE GRAPH */}
        {(activeTab === 'both' || activeTab === 'statemachine') && (
          <div className={`${activeTab === 'both' ? 'lg:col-span-5' : 'w-full'} bg-slate-950/70 border border-slate-800 rounded-xl p-4 space-y-4`}>
            <div className="flex items-center justify-between border-b border-slate-800 pb-2">
              <div className="flex items-center gap-2">
                <Shield size={15} className="text-emerald-400" />
                <h3 className="text-xs font-bold text-white uppercase tracking-wider">
                  State Machine (The Law)
                </h3>
              </div>
              <span className="text-[11px] text-slate-400">
                Entity: <code className="text-emerald-300 font-mono">{entityType}</code>
              </span>
            </div>

            {/* States Grid */}
            <div className="space-y-2">
              <span className="text-[10px] text-slate-400 uppercase font-semibold tracking-wider block">
                Defined Entity States:
              </span>
              <div className="flex flex-wrap gap-1.5">
                {smStates.map(st => {
                  const isCurrentState = currentState && currentState.toLowerCase() === st.toLowerCase();
                  const isInit = initialState && initialState.toLowerCase() === st.toLowerCase();

                  return (
                    <div
                      key={st}
                      className={`px-2.5 py-1 rounded-lg text-xs font-mono font-medium border flex items-center gap-1.5 transition-all ${
                        isCurrentState
                          ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500 ring-2 ring-emerald-500/20 shadow'
                          : isInit
                          ? 'bg-blue-500/10 text-blue-300 border-blue-500/30'
                          : 'bg-slate-900 text-slate-300 border-slate-800'
                      }`}
                    >
                      <CircleDot size={11} className={isCurrentState ? 'text-emerald-400' : isInit ? 'text-blue-400' : 'text-slate-500'} />
                      <span>{st}</span>
                      {isInit && <span className="text-[9px] px-1 rounded bg-blue-500/20 text-blue-300">Initial</span>}
                      {isCurrentState && <span className="text-[9px] px-1 rounded bg-emerald-500 text-slate-950 font-bold">Active</span>}
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Legal Transitions Table */}
            <div className="space-y-2 pt-2 border-t border-slate-800">
              <span className="text-[10px] text-slate-400 uppercase font-semibold tracking-wider block">
                Authorized State Transitions:
              </span>
              
              {smTransitions.length > 0 ? (
                <div className="space-y-1.5 max-h-80 overflow-y-auto pr-1">
                  {smTransitions.map((t, idx) => {
                    const from = getProp(t, 'fromState', 'FromState') || '*';
                    const to = getProp(t, 'toState', 'ToState') || '';
                    const evt = getProp(t, 'eventId', 'EventId', 'eventName', 'EventName') || 'Event';

                    const isRelevant = currentState && currentState.toLowerCase() === from.toLowerCase();

                    return (
                      <div
                        key={idx}
                        className={`p-2 rounded-lg border text-xs flex items-center justify-between gap-2 font-mono transition-colors ${
                          isRelevant
                            ? 'bg-emerald-950/20 border-emerald-500/40 text-emerald-200'
                            : 'bg-slate-900/60 border-slate-800/80 text-slate-300'
                        }`}
                      >
                        <div className="flex items-center gap-1.5 truncate">
                          <span className="font-semibold text-slate-200">{from}</span>
                          <span className="text-slate-600">&rarr;</span>
                          <span className="font-bold text-emerald-400">{to}</span>
                        </div>
                        <span className="px-1.5 py-0.5 rounded bg-slate-950 text-blue-300 border border-slate-800 text-[10px] shrink-0">
                          {evt}
                        </span>
                      </div>
                    );
                  })}
                </div>
              ) : (
                <div className="text-slate-500 text-xs italic py-4 text-center">
                  No explicit state machine transitions registered.
                </div>
              )}
            </div>

            <div className="p-2.5 rounded-lg bg-slate-900/40 border border-slate-800/60 text-[11px] text-slate-400 leading-relaxed">
              <strong className="text-slate-300">Absolute Law Guarantee:</strong> The engine will immediately reject any workflow command attempting a transition not explicitly authorized in this table.
            </div>

          </div>
        )}

      </div>
    </div>
  );
};
