import React, { useState, useEffect, useMemo } from 'react';
import { 
  Play, RotateCcw, Activity, ArrowRight, Clock, UserCheck, Cpu, 
  Sparkles, Sliders, Database, AlertTriangle, Check
} from 'lucide-react';
import { WorkflowGraphVisualizer } from './WorkflowGraphVisualizer';

interface Props {
  definition: any;
}

const PRESET_PAYLOADS: Record<string, { label: string; data: Record<string, any> }> = {
  highExpense: {
    label: 'High Expense ($7,500)',
    data: {
      Amount: 7500,
      Currency: 'USD',
      Category: 'Equipment',
      Department: 'Engineering',
      Urgent: true,
      Requester: 'Alice Smith'
    }
  },
  lowExpense: {
    label: 'Low Expense ($450)',
    data: {
      Amount: 450,
      Currency: 'USD',
      Category: 'OfficeSupplies',
      Department: 'Marketing',
      Urgent: false,
      Requester: 'Bob Jones'
    }
  },
  travelRequest: {
    label: 'Travel Request ($2,200)',
    data: {
      Amount: 2200,
      Currency: 'EUR',
      Category: 'Travel',
      Department: 'Sales',
      Destination: 'London',
      Urgent: true,
      Requester: 'Carol Danvers'
    }
  }
};

export const DraftSimulator: React.FC<Props> = ({ definition }) => {
  const [currentStepId, setCurrentStepId] = useState<string>('');
  const [currentState, setCurrentState] = useState<string>('');
  const [history, setHistory] = useState<string[]>([]);
  const [activeSideTab, setActiveSideTab] = useState<'controls' | 'context'>('controls');

  // Runtime Payload & Context State
  const [payload, setPayload] = useState<Record<string, any>>(PRESET_PAYLOADS.highExpense.data);
  const [payloadText, setPayloadText] = useState<string>(JSON.stringify(PRESET_PAYLOADS.highExpense.data, null, 2));
  const [payloadError, setPayloadError] = useState<string | null>(null);
  const [simulatedRole, setSimulatedRole] = useState<string>('Manager');

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

  // Collect all known roles from the blueprint
  const allKnownRoles = useMemo(() => {
    const roleSet = new Set<string>(['User', 'Manager', 'Director', 'Admin', 'System']);
    rawSteps.forEach(s => {
      getStepRoles(s).forEach(r => {
        if (r && r !== 'Anyone' && r !== 'Unassigned') roleSet.add(r);
      });
    });
    return Array.from(roleSet);
  }, [rawSteps]);

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

  const currentStepRoles = getStepRoles(currentStep);
  const stepType = (getProp(currentStep, 'stepType', 'StepType') || 'Command').toString();
  const stepTypeLower = stepType.toLowerCase();
  const isHumanTask = stepTypeLower.includes('human');
  const isDecisionStep = stepTypeLower.includes('decision') || stepTypeLower.includes('choice');
  const activeRoleDisplay = currentStepRoles.length > 0 ? currentStepRoles.join(', ') : (isHumanTask ? 'Unassigned' : 'System');

  // Payload text editing handler
  const handlePayloadChange = (text: string) => {
    setPayloadText(text);
    try {
      const parsed = JSON.parse(text);
      if (typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)) {
        setPayload(parsed);
        setPayloadError(null);
      } else {
        setPayloadError('Payload must be a valid JSON object.');
      }
    } catch (err: any) {
      setPayloadError(err.message);
    }
  };

  const loadPresetPayload = (key: string) => {
    const preset = PRESET_PAYLOADS[key];
    if (preset) {
      setPayload(preset.data);
      setPayloadText(JSON.stringify(preset.data, null, 2));
      setPayloadError(null);
    }
  };

  // Evaluates a condition expression string against the payload dictionary
  const evaluateExpression = (expression: string, currentPayload: Record<string, any>): { result: boolean; error?: string } => {
    if (!expression || expression.trim() === '') return { result: true };
    const trimmed = expression.trim();
    if (trimmed.toLowerCase() === 'default') return { result: true };
    if (trimmed.toLowerCase() === 'true') return { result: true };
    if (trimmed.toLowerCase() === 'false') return { result: false };

    try {
      let expr = trimmed;
      // Replace "Payload.Key" or "payload.Key" with "Key"
      expr = expr.replace(/\b[pP]ayload\./g, '');
      // Normalize single '=' to '==' safely (excluding !=, <=, >=, ==)
      expr = expr.replace(/([^!<>=])=([^=])/g, '$1==$2');

      const keys = Object.keys(currentPayload);
      const values = Object.values(currentPayload);

      const fn = new Function(...keys, `try { return Boolean(${expr}); } catch (e) { return false; }`);
      const res = fn(...values);
      return { result: Boolean(res) };
    } catch (err: any) {
      return { result: false, error: err.message };
    }
  };

  // Evaluate conditions on the current step
  const rawConditions: Record<string, string> = getProp(currentStep, 'conditions', 'Conditions') || {};
  const evaluatedConditions = useMemo(() => {
    if (!currentStep) return { evaluated: [], winningTarget: null, winningExpr: null };

    let winnerTarget: string | null = null;
    let winnerExpr: string | null = null;

    const items = Object.entries(rawConditions).map(([expression, target]) => {
      const isDefault = expression.trim().toLowerCase() === 'default';
      const evalRes = evaluateExpression(expression, payload);
      const matched = evalRes.result;

      if (!winnerTarget && !isDefault && matched) {
        winnerTarget = target;
        winnerExpr = expression;
      }

      return {
        expression,
        target,
        matched,
        isDefault,
        error: evalRes.error
      };
    });

    // If no explicit condition matched, look for default
    if (!winnerTarget) {
      const defaultEntry = items.find(i => i.isDefault);
      if (defaultEntry) {
        winnerTarget = defaultEntry.target;
        winnerExpr = 'Default';
      }
    }

    return { evaluated: items, winningTarget: winnerTarget, winningExpr: winnerExpr };
  }, [currentStep, rawConditions, payload]);

  // Execute a decision route
  const handleDecisionAdvance = (targetStepId: string, winningExpr: string) => {
    const payloadSummary = Object.entries(payload).slice(0, 3).map(([k, v]) => `${k}=${v}`).join(', ');
    const logMsg = `[Decision: ${currentStepId}] Condition "${winningExpr}" -> TRUE (${payloadSummary}) => Advanced to [${targetStepId}]`;
    setHistory(prev => [...prev, logMsg]);
    setCurrentStepId(targetStepId);
  };

  const fireEvent = (eventId: string, targetStepId: string) => {
    // 1. Check State Machine Transition
    let nextState = currentState;
    let guardBlocked = false;
    let guardReason = '';

    const matching = transitions.filter(t => 
        (getProp(t, 'fromState', 'FromState') === currentState || getProp(t, 'fromState', 'FromState') === '*') &&
        (getProp(t, 'eventId', 'EventId', 'eventName', 'EventName') === eventId)
    );

    if (matching.length > 0) {
      const transition = matching[0];
      const targetState = getProp(transition, 'toState', 'ToState');
      
      // Check transition guard condition against payload if configured
      const constraint = getProp(transition, 'condition', 'Condition', 'constraint', 'Constraint');
      if (constraint && typeof constraint === 'string') {
        const evalRes = evaluateExpression(constraint, payload);
        if (!evalRes.result) {
          guardBlocked = true;
          guardReason = `State Machine Guard Failed: Expression "${constraint}" evaluated to FALSE against current payload.`;
        }
      }

      if (!guardBlocked && targetState) {
        nextState = targetState;
      }
    }

    if (guardBlocked) {
      setHistory(prev => [...prev, `[GUARD BLOCKED] ${guardReason} (State: ${currentState})`]);
      alert(`⚠️ State Machine Guard Violation:\n\n${guardReason}\n\nState remains [${currentState}]. Update payload in "Context & Payload" tab to satisfy the guard.`);
      return;
    }

    // 2. Advance
    const actingRole = simulatedRole || activeRoleDisplay;
    setHistory(prev => [...prev, `[Role: ${actingRole}] Fired "${eventId}" -> Step: ${targetStepId} (State: ${nextState})`]);
    setCurrentStepId(targetStepId);
    setCurrentState(nextState);
  };

  // Check role authorization for human tasks
  const isRoleAuthorized = useMemo(() => {
    if (!isHumanTask) return true;
    if (currentStepRoles.length === 0 || currentStepRoles.includes('Anyone') || currentStepRoles.includes('Unassigned')) return true;
    return currentStepRoles.map(r => r.toLowerCase()).includes(simulatedRole.toLowerCase());
  }, [isHumanTask, currentStepRoles, simulatedRole]);

  // Render controls based on active step
  const renderControls = () => {
    if (!currentStep) return (
      <div className="text-slate-500 text-xs py-6 text-center">
        <div className="w-8 h-8 rounded-full bg-slate-800 text-slate-400 flex items-center justify-center mx-auto mb-2">
          <Check size={16} />
        </div>
        <p className="font-semibold text-slate-300">Simulation Complete</p>
        <p className="text-[11px] text-slate-500 mt-1">Workflow reached end or terminal step.</p>
      </div>
    );

    const nextSteps = getProp(currentStep, 'nextSteps', 'NextSteps') || {};
    const sla = getProp(currentStep, 'sla', 'Sla', 'SLA');
    const hasConditions = Object.keys(rawConditions).length > 0;

    return (
      <div className="space-y-4">
        {/* CASE 1: Decision Step / Step with Evaluated Conditions */}
        {(isDecisionStep || hasConditions) ? (
          <div className="bg-purple-500/10 border border-purple-500/30 p-3.5 rounded-xl space-y-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-1.5">
                <Sparkles size={15} className="text-purple-400" />
                <p className="text-xs text-purple-300 font-bold">Decision Step / Dynamic Conditions</p>
              </div>
              <span className="text-[10px] bg-purple-500/20 text-purple-300 px-2 py-0.5 rounded border border-purple-500/30 font-mono">
                {evaluatedConditions.evaluated.length} Rules
              </span>
            </div>

            <p className="text-[11px] text-slate-300">
              Evaluates condition expressions against the active context payload:
            </p>

            {/* Condition Rules List with Live Evaluation Results */}
            <div className="space-y-1.5">
              {evaluatedConditions.evaluated.map((c, idx) => {
                const isWinner = evaluatedConditions.winningTarget === c.target && (evaluatedConditions.winningExpr === c.expression);
                return (
                  <div 
                    key={idx} 
                    className={`p-2 rounded-lg border text-xs flex items-center justify-between transition-all ${
                      isWinner 
                        ? 'bg-purple-950/80 border-emerald-500/80 text-white shadow-md ring-1 ring-emerald-500/40' 
                        : 'bg-slate-950/70 border-slate-800 text-slate-400 opacity-70'
                    }`}
                  >
                    <div className="flex items-center gap-2">
                      <span className={`w-4 h-4 rounded-full flex items-center justify-center text-[10px] font-bold ${
                        c.matched ? 'bg-emerald-500/20 text-emerald-400' : 'bg-slate-800 text-slate-500'
                      }`}>
                        {c.matched ? '✓' : '×'}
                      </span>
                      <div>
                        <span className="font-mono text-[11px] font-semibold text-slate-200">{c.expression}</span>
                        <span className="text-[10px] text-slate-500 block">Target ➔ <strong className="text-blue-300">{c.target}</strong></span>
                      </div>
                    </div>
                    {isWinner && (
                      <span className="px-1.5 py-0.5 rounded bg-emerald-500/20 text-emerald-300 border border-emerald-500/40 text-[9px] font-bold uppercase tracking-wider">
                        Matches Payload
                      </span>
                    )}
                  </div>
                );
              })}
            </div>

            {evaluatedConditions.winningTarget ? (
              <button 
                onClick={() => handleDecisionAdvance(evaluatedConditions.winningTarget!, evaluatedConditions.winningExpr!)}
                className="w-full py-2 bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 text-white text-xs font-bold rounded-lg shadow-lg shadow-purple-500/20 transition-all flex items-center justify-center gap-1.5"
              >
                <span>Take Branch: <strong>{evaluatedConditions.winningTarget}</strong></span>
                <ArrowRight size={14} />
              </button>
            ) : (
              <div className="p-2 bg-rose-500/10 border border-rose-500/30 rounded text-rose-300 text-[11px] text-center">
                No conditions evaluated to true and no Default branch specified.
              </div>
            )}
          </div>
        ) : stepTypeLower.includes('command') || stepTypeLower.includes('event') ? (
          /* CASE 2: Command / Automated Action Step */
          <div className="bg-blue-500/10 border border-blue-500/30 p-3.5 rounded-xl space-y-2.5">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-xs text-blue-300 font-semibold mb-0.5 flex items-center gap-1.5">
                  <Cpu size={14} className="text-blue-400" /> System Action ({stepType || 'Command'})
                </p>
                <div className="flex items-center gap-1.5 text-[11px]">
                  <span className="text-slate-400">Executing Role:</span>
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
          /* CASE 3: Human Task Step */
          <div className="bg-amber-500/10 border border-amber-500/30 p-3.5 rounded-xl space-y-3">
            <div>
              <div className="flex items-center justify-between mb-1">
                <p className="text-xs text-amber-300 font-semibold flex items-center gap-1.5">
                  <UserCheck size={14} className="text-amber-400" /> Human Task Sign-off
                </p>
                <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-amber-500/20 text-amber-200 border border-amber-500/40">
                  Required: {activeRoleDisplay}
                </span>
              </div>

              {!isRoleAuthorized && (
                <div className="mb-2 p-2 bg-rose-500/15 border border-rose-500/30 rounded-lg text-rose-300 text-[11px] flex items-center justify-between">
                  <span>Role Mismatch: Requires <strong>{activeRoleDisplay}</strong> (You are <strong>{simulatedRole}</strong>)</span>
                  <button 
                    onClick={() => setSimulatedRole(currentStepRoles[0] || 'Manager')} 
                    className="underline text-[10px] font-bold text-rose-200 hover:text-white"
                  >
                    Switch Role
                  </button>
                </div>
              )}

              <p className="text-[11px] text-slate-300">
                Acting as <strong className="text-amber-300 font-semibold">{simulatedRole}</strong>, select an outcome to dispatch:
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

        {/* SLA Escalation Option */}
        {sla && (
           <div className="bg-rose-500/10 border border-rose-500/30 p-3 rounded-xl flex items-center justify-between">
             <div>
               <p className="text-xs text-rose-300 font-semibold mb-0.5 flex items-center gap-1">
                 <Clock size={14}/> SLA Timeout: {getProp(sla, 'duration', 'Duration')}
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
      {/* Top Header */}
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

        {/* Right: Controls, Context & Logs */}
        <div className="w-[380px] bg-slate-900 flex flex-col h-full overflow-hidden">
          
          {/* Side Panel Tab Selector */}
          <div className="flex bg-slate-950 border-b border-slate-800 p-1">
            <button
              onClick={() => setActiveSideTab('controls')}
              className={`flex-1 py-1.5 text-xs font-semibold rounded-lg flex items-center justify-center gap-1.5 transition-all ${
                activeSideTab === 'controls' ? 'bg-slate-800 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              <Sliders size={13} /> Actions & Execution
            </button>
            <button
              onClick={() => setActiveSideTab('context')}
              className={`flex-1 py-1.5 text-xs font-semibold rounded-lg flex items-center justify-center gap-1.5 transition-all ${
                activeSideTab === 'context' ? 'bg-slate-800 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              <Database size={13} /> Context & Payload ({Object.keys(payload).length})
            </button>
          </div>

          {activeSideTab === 'controls' ? (
            <>
              {/* Active Context Card */}
              <div className="p-4 border-b border-slate-800 shrink-0 bg-slate-950/40 space-y-2.5">
                <div className="flex items-center justify-between">
                  <h4 className="text-xs font-bold text-slate-300 uppercase tracking-wider">Active Context</h4>
                  <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold border flex items-center gap-1 ${
                    isDecisionStep || Object.keys(rawConditions).length > 0
                      ? 'bg-purple-500/20 text-purple-300 border-purple-500/30'
                      : isHumanTask 
                      ? 'bg-amber-500/20 text-amber-300 border-amber-500/30' 
                      : 'bg-blue-500/20 text-blue-300 border-blue-500/30'
                  }`}>
                    {isDecisionStep || Object.keys(rawConditions).length > 0 ? <Sparkles size={11} /> : isHumanTask ? <UserCheck size={11} /> : <Cpu size={11} />}
                    {isDecisionStep || Object.keys(rawConditions).length > 0 ? 'Decision Step' : isHumanTask ? 'Human Task' : 'Automated Action'}
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
                    <span className="text-slate-400 text-[11px]">Simulating As Role:</span>
                    <select 
                      value={simulatedRole} 
                      onChange={e => setSimulatedRole(e.target.value)}
                      className="bg-slate-950 border border-indigo-500/40 text-indigo-300 font-bold text-[11px] rounded px-2 py-0.5 focus:outline-none focus:border-indigo-400"
                    >
                      {allKnownRoles.map(r => (
                        <option key={r} value={r}>{r}</option>
                      ))}
                    </select>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-slate-400 text-[11px]">Legal State:</span>
                    <span className="font-mono text-emerald-400 font-bold px-2 py-0.5 rounded bg-emerald-500/15 border border-emerald-500/30">
                      {currentState || 'None'}
                    </span>
                  </div>

                  {/* Quick Payload Variables Peek */}
                  <div className="pt-2 border-t border-slate-800/80 flex items-center justify-between">
                    <span className="text-slate-500 text-[10px]">Payload:</span>
                    <div className="flex items-center gap-1.5">
                      <span className="font-mono text-[10px] text-slate-300 bg-slate-950 px-1.5 py-0.5 rounded truncate max-w-[170px]">
                        {Object.entries(payload).slice(0, 2).map(([k, v]) => `${k}:${v}`).join(', ')}
                      </span>
                      <button 
                        onClick={() => setActiveSideTab('context')} 
                        className="text-[10px] text-blue-400 hover:text-blue-300 underline font-medium"
                      >
                        Edit
                      </button>
                    </div>
                  </div>
                </div>
              </div>

              {/* Action Controls */}
              <div className="p-4 border-b border-slate-800 shrink-0">
                <h4 className="text-xs font-bold text-slate-300 uppercase tracking-wider mb-3">Simulation Controls</h4>
                {renderControls()}
              </div>
            </>
          ) : (
            /* Context & Payload Editor Tab */
            <div className="p-4 flex-1 flex flex-col overflow-hidden space-y-4">
              <div>
                <div className="flex items-center justify-between mb-1.5">
                  <h4 className="text-xs font-bold text-white flex items-center gap-1.5">
                    <Database size={14} className="text-blue-400" /> Context Payload (JSON)
                  </h4>
                  <span className="text-[10px] text-slate-500 font-mono">Dynamic Dictionary</span>
                </div>
                <p className="text-[11px] text-slate-400">
                  Data evaluated by <strong>Decision steps</strong> and <strong>State Machine guards</strong>.
                </p>
              </div>

              {/* Preset Templates */}
              <div>
                <span className="text-[10px] text-slate-500 uppercase font-bold block mb-1">Load Preset Payload:</span>
                <div className="flex flex-wrap gap-1.5">
                  {Object.entries(PRESET_PAYLOADS).map(([k, v]) => (
                    <button
                      key={k}
                      onClick={() => loadPresetPayload(k)}
                      className="px-2 py-1 bg-slate-800 hover:bg-slate-750 text-slate-300 hover:text-white rounded text-[10px] font-medium transition-colors border border-slate-700"
                    >
                      {v.label}
                    </button>
                  ))}
                </div>
              </div>

              {/* Simulated Role Setting */}
              <div>
                <label className="block text-[10px] text-slate-400 uppercase font-bold mb-1">Simulated User Role</label>
                <div className="flex gap-2">
                  <select 
                    value={simulatedRole} 
                    onChange={e => setSimulatedRole(e.target.value)}
                    className="w-full bg-slate-950 border border-slate-700 rounded-lg p-2 text-xs text-white focus:outline-none focus:border-blue-500 font-medium"
                  >
                    {allKnownRoles.map(r => (
                      <option key={r} value={r}>{r}</option>
                    ))}
                  </select>
                </div>
              </div>

              {/* Live JSON Editor */}
              <div className="flex-1 flex flex-col overflow-hidden">
                <label className="block text-[10px] text-slate-400 uppercase font-bold mb-1">Payload Variables</label>
                <textarea
                  value={payloadText}
                  onChange={e => handlePayloadChange(e.target.value)}
                  className={`w-full flex-1 font-mono text-xs p-3 rounded-xl bg-slate-950 text-blue-300 focus:outline-none border ${
                    payloadError ? 'border-rose-500/70 text-rose-300' : 'border-slate-800 focus:border-blue-500'
                  }`}
                  placeholder="Enter JSON payload..."
                />
                {payloadError && (
                  <p className="text-[10px] text-rose-400 mt-1 flex items-center gap-1">
                    <AlertTriangle size={11} /> {payloadError}
                  </p>
                )}
              </div>

              <div className="pt-2 border-t border-slate-800 flex justify-end">
                <button
                  onClick={() => setActiveSideTab('controls')}
                  className="px-4 py-1.5 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-xs font-semibold shadow transition-colors flex items-center gap-1"
                >
                  Apply & Run Controls <ArrowRight size={13} />
                </button>
              </div>
            </div>
          )}
          
          {/* Execution Log */}
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
