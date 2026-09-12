import React, { useState, useEffect } from 'react';
import { Play, RotateCcw, Activity, ArrowRight, Clock, UserCheck, Cpu } from 'lucide-react';
import { WorkflowGraphVisualizer } from './WorkflowGraphVisualizer';

interface Props {
  definition: any;
}

export const DraftSimulator: React.FC<Props> = ({ definition }) => {
  const [currentStepId, setCurrentStepId] = useState<string>('');
  const [currentState, setCurrentState] = useState<string>('');
  const [history, setHistory] = useState<string[]>([]);

  // Safe case-insensitive helper
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

  const wfObj = getProp(definition, 'workflow', 'Workflow') || definition;
  const rawSteps: any[] = getProp(wfObj, 'steps', 'Steps') || [];
  const startStepId = getProp(wfObj, 'startStepId', 'StartStepId') || 'Start';
  
  const smObj = getProp(definition, 'stateMachine', 'StateMachine') || {};
  const initialState = getProp(smObj, 'initialState', 'InitialState') || '';
  const transitions: any[] = getProp(smObj, 'transitions', 'Transitions') || [];

  // Initialize
  useEffect(() => {
    resetSimulation();
  }, [definition]);

  const resetSimulation = () => {
    setCurrentStepId(startStepId);
    setCurrentState(initialState);
    setHistory([]);
  };

  const currentStep = rawSteps.find((s: any) => 
    (getProp(s, 'stepId', 'StepId') || '').toLowerCase() === (currentStepId || '').toLowerCase()
  );

  const getStepRoles = (step: any): string[] => {
    if (!step) return [];
    const raw = getProp(step, 'allowedRoles', 'AllowedRoles', 'requiredRoles', 'RequiredRoles', 'roles', 'Roles');
    if (!raw) {
      const type = (getProp(step, 'stepType', 'StepType') || '').toString().toLowerCase();
      if (type.includes('command') || type.includes('event')) return ['System'];
      return ['Anyone'];
    }
    if (Array.isArray(raw)) return raw.map((r: any) => r.toString().trim()).filter(Boolean);
    if (typeof raw === 'string') return raw.split(',').map((r: string) => r.trim()).filter(Boolean);
    return [raw.toString()];
  };

  const currentStepRoles = getStepRoles(currentStep);
  const stepType = (getProp(currentStep, 'stepType', 'StepType') || '').toString().toLowerCase();
  const isHumanTask = stepType.includes('human');
  const activeRoleDisplay = currentStepRoles.length > 0 ? currentStepRoles.join(', ') : (isHumanTask ? 'Unassigned' : 'System');

  const fireEvent = (eventId: string, targetStepId: string) => {
    // 1. Check State Machine Transition
    let nextState = currentState;
    const transition = transitions.find(t => 
        (getProp(t, 'fromState', 'FromState') === currentState || getProp(t, 'fromState', 'FromState') === '*') &&
        (getProp(t, 'eventId', 'EventId', 'eventName', 'EventName') === eventId)
    );

    if (transition) {
        nextState = getProp(transition, 'toState', 'ToState') || currentState;
    }

    // 2. Advance
    const actingRole = activeRoleDisplay;
    setHistory([...history, `[Role: ${actingRole}] Fired "${eventId}" -> Step: ${targetStepId} (State: ${nextState})`]);
    setCurrentStepId(targetStepId);
    setCurrentState(nextState);
  };

  // Render controls based on active step
  const renderControls = () => {
    if (!currentStep) return (
      <div className="text-slate-500 text-xs py-4 text-center">
        <p className="font-semibold text-slate-400">Simulation Complete</p>
        <p className="text-[11px] text-slate-500 mt-1">Workflow reached end or terminal step.</p>
      </div>
    );

    const nextSteps = getProp(currentStep, 'nextSteps', 'NextSteps') || {};
    const sla = getProp(currentStep, 'sla', 'Sla', 'SLA');

    return (
      <div className="space-y-4">
        {stepType.includes('command') || stepType.includes('event') ? (
          <div className="bg-blue-500/10 border border-blue-500/30 p-3.5 rounded-xl space-y-2.5">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-xs text-blue-300 font-semibold mb-0.5 flex items-center gap-1.5">
                  <Cpu size={14} className="text-blue-400" /> System Action ({stepType || 'Command'})
                </p>
                <div className="flex items-center gap-1.5 text-[11px]">
                  <span className="text-slate-400">Active Role:</span>
                  <span className="text-blue-300 font-mono font-bold">{activeRoleDisplay}</span>
                </div>
              </div>
              <button 
                onClick={() => {
                  const defaultTarget = nextSteps['Default'] || Object.values(nextSteps)[0] || 'END';
                  fireEvent('Default', defaultTarget as string);
                }}
                className="px-3.5 py-1.5 bg-blue-600 hover:bg-blue-500 text-white text-xs font-bold rounded-lg shadow-md transition-all flex items-center gap-1.5 shrink-0"
              >
                Auto-Step <ArrowRight size={14} />
              </button>
            </div>
            <p className="text-[10px] text-slate-500 italic">
              Automated step executes directly under {activeRoleDisplay} system credentials.
            </p>
          </div>
        ) : (
          <div className="bg-amber-500/10 border border-amber-500/30 p-3.5 rounded-xl space-y-3">
            <div>
              <div className="flex items-center justify-between mb-1">
                <p className="text-xs text-amber-300 font-semibold flex items-center gap-1.5">
                  <UserCheck size={14} className="text-amber-400" /> Human Task Action
                </p>
                <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-amber-500/20 text-amber-200 border border-amber-500/40">
                  Role: {activeRoleDisplay}
                </span>
              </div>
              <p className="text-[11px] text-slate-300">
                Acting as <strong className="text-amber-300 font-semibold">{activeRoleDisplay}</strong>, choose an outcome to simulate:
              </p>
            </div>
            <div className="flex flex-wrap gap-2 pt-1">
              {Object.entries(nextSteps).map(([outcome, target]) => (
                <button 
                  key={outcome}
                  onClick={() => fireEvent(outcome, target as string)}
                  className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 hover:border-amber-500/50 border border-slate-600 text-white text-xs font-semibold rounded-lg transition-all flex items-center gap-1 shadow-sm"
                >
                  Fire: <span className="text-amber-400 font-mono font-bold">{outcome}</span>
                </button>
              ))}
            </div>
          </div>
        )}

        {sla && (
           <div className="bg-rose-500/10 border border-rose-500/30 p-3 rounded-xl flex items-center justify-between">
             <div>
               <p className="text-xs text-rose-300 font-semibold mb-1 flex items-center gap-1">
                 <Clock size={14}/> SLA: {getProp(sla, 'duration', 'Duration')}
               </p>
               <p className="text-[10px] text-slate-400">
                 Escalates to: {getProp(sla, 'escalationStepId', 'EscalationStepId') || 'END'}
               </p>
             </div>
             <button 
               onClick={() => {
                 const evt = getProp(sla, 'timeoutEvent', 'TimeoutEvent') || 'TIMEOUT';
                 const tgt = getProp(sla, 'escalationStepId', 'EscalationStepId') || 'END';
                 fireEvent(evt, tgt);
               }}
               className="px-3 py-1.5 bg-rose-900/50 hover:bg-rose-800/80 text-rose-200 border border-rose-700 text-xs font-bold rounded shadow transition-colors flex items-center gap-1 shrink-0"
             >
               Force Timeout
             </button>
           </div>
        )}
      </div>
    );
  };

  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden flex flex-col min-h-[600px] h-full">
      <div className="p-3 bg-slate-950 border-b border-slate-800 flex justify-between items-center">
        <div className="flex items-center gap-3">
          <h3 className="text-sm font-bold text-white flex items-center gap-2">
            <Play size={16} className="text-emerald-400" /> Draft Sandbox Simulator
          </h3>
          {currentStep && (
            <div className="hidden sm:flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-slate-900 border border-slate-800 text-xs">
              <span className="text-slate-400 text-[11px]">Active Role:</span>
              <span className="text-indigo-300 font-semibold font-mono flex items-center gap-1 text-[11px]">
                {isHumanTask ? <UserCheck size={12} className="text-indigo-400" /> : <Cpu size={12} className="text-blue-400" />}
                {activeRoleDisplay}
              </span>
            </div>
          )}
        </div>
        <button onClick={resetSimulation} className="text-xs text-slate-400 hover:text-white flex items-center gap-1 bg-slate-800 hover:bg-slate-700 px-2.5 py-1 rounded transition-colors">
          <RotateCcw size={14} /> Restart Simulation
        </button>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* Left: Graph */}
        <div className="flex-1 bg-slate-950/50 overflow-auto border-r border-slate-800 p-2 relative h-full">
           <WorkflowGraphVisualizer 
              definition={definition} 
              currentStepId={currentStepId} 
              currentState={currentState} 
              initialView="both" 
           />
        </div>

        {/* Right: Controls & Logs */}
        <div className="w-[360px] bg-slate-900 flex flex-col h-full overflow-hidden">
          {/* Active Context Card */}
          <div className="p-4 border-b border-slate-800 shrink-0 bg-slate-950/40 space-y-3">
            <div className="flex items-center justify-between">
              <h4 className="text-xs font-bold text-slate-300 uppercase tracking-wider">Active Context</h4>
              <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold border flex items-center gap-1 ${
                isHumanTask 
                  ? 'bg-amber-500/20 text-amber-300 border-amber-500/30' 
                  : 'bg-blue-500/20 text-blue-300 border-blue-500/30'
              }`}>
                {isHumanTask ? <UserCheck size={11} /> : <Cpu size={11} />}
                {isHumanTask ? 'Human Task' : 'Automated Action'}
              </span>
            </div>

            <div className="bg-slate-900/90 p-3 rounded-xl border border-slate-800 text-xs space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-slate-400 text-[11px]">Current Step:</span>
                <span className="font-mono text-amber-300 font-bold px-2 py-0.5 rounded bg-amber-500/15 border border-amber-500/30">
                  {currentStepId || 'END'}
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-slate-400 text-[11px]">Active Role:</span>
                <span className={`px-2 py-0.5 rounded font-bold text-xs flex items-center gap-1 border ${
                  isHumanTask 
                    ? 'bg-indigo-500/20 text-indigo-300 border-indigo-500/40' 
                    : 'bg-blue-500/20 text-blue-300 border-blue-500/40'
                }`}>
                  {isHumanTask ? <UserCheck size={12} /> : <Cpu size={12} />}
                  {activeRoleDisplay}
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-slate-400 text-[11px]">Legal State:</span>
                <span className="font-mono text-emerald-400 font-bold px-2 py-0.5 rounded bg-emerald-500/15 border border-emerald-500/30">
                  {currentState || 'None'}
                </span>
              </div>
            </div>
          </div>

          <div className="p-4 border-b border-slate-800 shrink-0">
            <h4 className="text-xs font-bold text-slate-300 uppercase tracking-wider mb-3">Simulation Controls</h4>
            {renderControls()}
          </div>
          
          <div className="flex-1 p-4 overflow-y-auto bg-slate-950/30">
            <h4 className="text-xs font-bold text-slate-500 uppercase tracking-wider mb-3 flex items-center gap-1">
              <Activity size={14} /> Execution Log
            </h4>
            <div className="space-y-2">
              <div className="text-[10px] text-emerald-400 font-mono pb-2 border-b border-slate-800/50">
                [SYSTEM] Simulator started. Initial Step: {startStepId} (State: {initialState || 'None'})
              </div>
              {history.map((log, idx) => (
                <div key={idx} className="text-[10px] text-slate-300 font-mono pb-2 border-b border-slate-800/50 last:border-0 flex items-start gap-1.5">
                  <span className="text-slate-600">❯</span>
                  <span>{log}</span>
                </div>
              ))}
              {!currentStep && history.length > 0 && (
                <div className="text-[10px] text-blue-400 font-mono pt-1">
                  [SYSTEM] Reached End of Workflow.
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
