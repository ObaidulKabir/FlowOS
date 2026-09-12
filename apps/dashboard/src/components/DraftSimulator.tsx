import React, { useState, useEffect } from 'react';
import { Play, RotateCcw, Activity, ArrowRight, Clock } from 'lucide-react';
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
    setHistory([...history, `Fired ${eventId} -> Step: ${targetStepId} (State: ${nextState})`]);
    setCurrentStepId(targetStepId);
    setCurrentState(nextState);
  };

  // Render controls based on active step
  const renderControls = () => {
    if (!currentStep) return <div className="text-slate-500 text-xs py-4">Simulation Ended or Invalid Step</div>;

    const nextSteps = getProp(currentStep, 'nextSteps', 'NextSteps') || {};
    const stepType = (getProp(currentStep, 'stepType', 'StepType') || '').toLowerCase();
    const sla = getProp(currentStep, 'sla', 'Sla', 'SLA');

    return (
      <div className="space-y-4">
        {stepType.includes('command') || stepType.includes('event') ? (
          <div className="bg-blue-500/10 border border-blue-500/30 p-3 rounded-xl flex items-center justify-between">
            <div>
              <p className="text-xs text-blue-300 font-semibold mb-1">System Action ({stepType})</p>
              <p className="text-[11px] text-slate-400">Executes automatically.</p>
            </div>
            <button 
              onClick={() => {
                const defaultTarget = nextSteps['Default'] || Object.values(nextSteps)[0] || 'END';
                fireEvent('Default', defaultTarget as string);
              }}
              className="px-4 py-1.5 bg-blue-600 hover:bg-blue-500 text-white text-xs font-bold rounded shadow transition-colors flex items-center gap-1"
            >
              Simulate <ArrowRight size={14} />
            </button>
          </div>
        ) : (
          <div className="bg-amber-500/10 border border-amber-500/30 p-3 rounded-xl space-y-3">
            <div>
              <p className="text-xs text-amber-300 font-semibold mb-1">Human Task / Awaiting Event</p>
              <p className="text-[11px] text-slate-400">Choose an outcome to simulate:</p>
            </div>
            <div className="flex flex-wrap gap-2">
              {Object.entries(nextSteps).map(([outcome, target]) => (
                <button 
                  key={outcome}
                  onClick={() => fireEvent(outcome, target as string)}
                  className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 border border-slate-600 text-white text-xs font-medium rounded transition-colors"
                >
                  Fire: <span className="text-amber-400 font-mono">{outcome}</span>
                </button>
              ))}
            </div>
          </div>
        )}

        {sla && (
           <div className="bg-rose-500/10 border border-rose-500/30 p-3 rounded-xl flex items-center justify-between">
             <div>
               <p className="text-xs text-rose-300 font-semibold mb-1 flex items-center gap-1"><Clock size={14}/> SLA: {getProp(sla, 'duration', 'Duration')}</p>
             </div>
             <button 
               onClick={() => {
                 const evt = getProp(sla, 'timeoutEvent', 'TimeoutEvent') || 'TIMEOUT';
                 const tgt = getProp(sla, 'escalationStepId', 'EscalationStepId') || 'END';
                 fireEvent(evt, tgt);
               }}
               className="px-3 py-1.5 bg-rose-900/50 hover:bg-rose-800/80 text-rose-200 border border-rose-700 text-xs font-bold rounded shadow transition-colors flex items-center gap-1"
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
        <h3 className="text-sm font-bold text-white flex items-center gap-2">
          <Play size={16} className="text-emerald-400" /> Draft Sandbox Simulator
        </h3>
        <button onClick={resetSimulation} className="text-xs text-slate-400 hover:text-white flex items-center gap-1 bg-slate-800 px-2 py-1 rounded">
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
        <div className="w-[350px] bg-slate-900 flex flex-col h-full overflow-hidden">
          <div className="p-4 border-b border-slate-800 shrink-0">
            <h4 className="text-xs font-bold text-slate-300 uppercase tracking-wider mb-4">Simulation Controls</h4>
            {renderControls()}
          </div>
          
          <div className="flex-1 p-4 overflow-y-auto bg-slate-950/30">
            <h4 className="text-xs font-bold text-slate-500 uppercase tracking-wider mb-3 flex items-center gap-1">
              <Activity size={14} /> Execution Log
            </h4>
            <div className="space-y-2">
              <div className="text-[10px] text-emerald-500 font-mono">
                [SYSTEM] Simulator started. Initial Step: {startStepId}, State: {initialState}
              </div>
              {history.map((log, idx) => (
                <div key={idx} className="text-[10px] text-slate-300 font-mono pb-2 border-b border-slate-800/50 last:border-0">
                  {log}
                </div>
              ))}
              {!currentStep && history.length > 0 && (
                <div className="text-[10px] text-blue-400 font-mono">
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
