import React, { useState, useEffect } from 'react';
import { WorkflowClass, CreateDraftRequest, ValidationResult } from '../types';
import { X, Save, AlertTriangle, CheckCircle, Info } from 'lucide-react';

interface Props {
  item?: WorkflowClass; // If null, creating new
  validation: ValidationResult | null;
  onClose: () => void;
  onSave: (req: CreateDraftRequest) => Promise<void>;
}

// "Smart" Editor Component
export const EditorView: React.FC<Props> = ({ item, validation, onClose, onSave }) => {
  const [name, setName] = useState(item?.name || 'New Workflow');
  const [version, setVersion] = useState(item?.version || '0.1.0');
  
  // Structured State for "Smart" Editing
  const [events, setEvents] = useState<{eventId: string, name: string}[]>(
    item?.definition?.Events || []
  );
  const [states, setStates] = useState<string[]>(
    item?.definition?.StateMachine?.States || ['Draft', 'Active', 'Completed']
  );
  const [initialState, setInitialState] = useState(
    item?.definition?.StateMachine?.InitialState || 'Draft'
  );
  const [transitions, setTransitions] = useState<{from: string, to: string, evt: string}[]>(
    item?.definition?.StateMachine?.Transitions?.map((t: any) => ({
        from: t.FromState, to: t.ToState, evt: t.EventId
    })) || []
  );

  // Steps State
  const [startStepId, setStartStepId] = useState(
    item?.definition?.Workflow?.StartStepId || 'Start'
  );
  const [steps, setSteps] = useState<{
    stepId: string, 
    stepType: string, 
    nextSteps: {outcome: string, target: string}[],
    roles: string
  }[]>([]);

  const [jsonMode, setJsonMode] = useState(false);
  const [rawJson, setRawJson] = useState(JSON.stringify(item?.definition || {}, null, 2));

  // Sync JSON to Form (When rawJson changes in JSON mode, or initially)
  useEffect(() => {
      if (item) {
          setName(item.name);
          setVersion(item.version);
          const def = item.definition || {};
          
          const getProp = (obj: any, key: string) => {
              if (!obj) return undefined;
              return obj[key] || obj[key.toLowerCase()] || obj[key.charAt(0).toLowerCase() + key.slice(1)];
          };

          const eventsList = getProp(def, 'Events') || [];
          const sm = getProp(def, 'StateMachine') || {};
          const statesList = getProp(sm, 'States') || ['Draft'];
          const transitionsList = getProp(sm, 'Transitions') || [];
          
          setEvents(eventsList.map((e: any) => ({
              eventId: getProp(e, 'EventId') || '',
              name: getProp(e, 'Name') || ''
          })));
          
          setStates(statesList);
          setInitialState(getProp(sm, 'InitialState') || 'Draft');
          
          setTransitions(transitionsList.map((t: any) => ({
            from: getProp(t, 'FromState') || '', 
            to: getProp(t, 'ToState') || '', 
            evt: getProp(t, 'EventId') || ''
          })));

          const wf = getProp(def, 'Workflow') || {};
          setStartStepId(getProp(wf, 'StartStepId') || 'Start');
          const stepsList = getProp(wf, 'Steps') || [];
          setSteps(stepsList.map((s: any) => {
              const nextStepsDict = getProp(s, 'NextSteps') || {};
              const nextStepsArray = Object.keys(nextStepsDict).map(k => ({
                  outcome: k,
                  target: nextStepsDict[k]
              }));
              const rolesList = getProp(s, 'RequiredRoles') || [];
              
              return {
                  stepId: getProp(s, 'StepId') || '',
                  stepType: getProp(s, 'StepType') || 'Command',
                  nextSteps: nextStepsArray,
                  roles: rolesList.join(', ')
              };
          }));

          setRawJson(JSON.stringify(def, null, 2));
      }
  }, [item]);

  // Sync Form to JSON
  useEffect(() => {
    if (!jsonMode) {
        const def = {
            Events: events.map(e => ({ EventId: e.eventId, Name: e.name })),
            StateMachine: {
                InitialState: initialState,
                States: states,
                Transitions: transitions.map(t => ({ FromState: t.from, ToState: t.to, EventId: t.evt }))
            },
            Workflow: {
                StartStepId: startStepId,
                Steps: steps.map(s => {
                    const nextStepsDict: Record<string, string> = {};
                    s.nextSteps.forEach(ns => {
                        if (ns.outcome) nextStepsDict[ns.outcome] = ns.target;
                    });
                    
                    return {
                        StepId: s.stepId,
                        StepType: s.stepType,
                        NextSteps: nextStepsDict,
                        RequiredRoles: s.roles ? s.roles.split(',').map(r => r.trim()).filter(r => r) : []
                    };
                })
            }
        };
        setRawJson(JSON.stringify(def, null, 2));
    }
  }, [events, states, initialState, transitions, steps, startStepId, jsonMode]);

  const handleSave = () => {
    try {
        const def = JSON.parse(rawJson);
        onSave({
            name,
            version,
            definition: def
        });
    } catch (e) {
        alert("Invalid JSON");
    }
  };

  const loadTemplate = (templateName: string) => {
    const commonEvents = [
        { eventId: 'EVT-SUBMIT', name: 'Submit' },
        { eventId: 'EVT-APPROVE', name: 'Approve' },
        { eventId: 'EVT-REJECT', name: 'Reject' }
    ];
    const commonStates = ['Draft', 'Pending', 'Approved', 'Rejected'];
    const commonTransitions = [
        { from: 'Draft', to: 'Pending', evt: 'EVT-SUBMIT' },
        { from: 'Pending', to: 'Approved', evt: 'EVT-APPROVE' },
        { from: 'Pending', to: 'Rejected', evt: 'EVT-REJECT' }
    ];
    const commonInitialState = 'Draft';

    if (templateName === 'Simple') {
        setEvents(commonEvents);
        setStates(commonStates);
        setInitialState(commonInitialState);
        setTransitions(commonTransitions);
        setStartStepId('Draft');
        setSteps([
            { stepId: 'Draft', stepType: 'Command', nextSteps: [{ outcome: 'EVT-SUBMIT', target: 'Pending' }], roles: 'User' },
            { stepId: 'Pending', stepType: 'HumanTask', nextSteps: [{ outcome: 'EVT-APPROVE', target: 'Approved' }, { outcome: 'EVT-REJECT', target: 'Rejected' }], roles: 'Manager' },
            { stepId: 'Approved', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'END' }], roles: '' },
            { stepId: 'Rejected', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'END' }], roles: '' }
        ]);
    } else if (templateName === 'Complex') {
        setEvents(commonEvents);
        setStates(commonStates);
        setInitialState(commonInitialState);
        setTransitions(commonTransitions);
        
        setStartStepId('ValidateInput'); 
        setSteps([
            { stepId: 'ValidateInput', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'Draft' }], roles: 'System' },
            { stepId: 'Draft', stepType: 'HumanTask', nextSteps: [{ outcome: 'EVT-SUBMIT', target: 'FraudCheck' }], roles: 'User' },
            { stepId: 'FraudCheck', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'Pending' }], roles: 'System' },
            { stepId: 'Pending', stepType: 'HumanTask', nextSteps: [{ outcome: 'EVT-APPROVE', target: 'NotifyApproval' }, { outcome: 'EVT-REJECT', target: 'NotifyRejection' }], roles: 'Manager' },
            { stepId: 'NotifyApproval', stepType: 'Event', nextSteps: [{ outcome: 'Default', target: 'Approved' }], roles: 'System' },
            { stepId: 'NotifyRejection', stepType: 'Event', nextSteps: [{ outcome: 'Default', target: 'Rejected' }], roles: 'System' },
            { stepId: 'Approved', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'END' }], roles: '' },
            { stepId: 'Rejected', stepType: 'Command', nextSteps: [{ outcome: 'Default', target: 'END' }], roles: '' }
        ]);
    }
  };

  return (
    <div className="fixed inset-0 bg-black/80 backdrop-blur-sm overflow-hidden h-full w-full flex justify-center items-center z-50 p-4">
      <div className="bg-slate-900 border border-slate-700 text-slate-100 w-[96vw] h-[92vh] rounded-2xl shadow-2xl flex overflow-hidden">
        
        {/* Left: Editor (Scrollable) */}
        <div className="flex-1 flex flex-col border-r border-slate-800 overflow-hidden">
            <div className="p-4 border-b border-slate-800 flex justify-between items-center bg-slate-950/70">
                <h2 className="text-lg font-bold text-white">{item ? `Edit ${item.name}` : 'Create WorkflowClass Draft'}</h2>
                <div className="space-x-2 flex items-center">
                    {!item && (
                        <select onChange={(e) => loadTemplate(e.target.value)} className="text-xs bg-slate-800 border border-slate-700 text-slate-200 p-1.5 rounded-lg mr-2" defaultValue="">
                            <option value="" disabled>Load Blueprint Example...</option>
                            <option value="Simple">Example 1: Simple (Direct Mapping)</option>
                            <option value="Complex">Example 2: Complex (Decoupled)</option>
                        </select>
                    )}
                    <button onClick={() => setJsonMode(!jsonMode)} className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 border border-slate-700 rounded-lg text-xs font-medium transition-colors">
                        {jsonMode ? "Switch to Form" : "Switch to JSON"}
                    </button>
                    <button onClick={onClose} className="text-slate-400 hover:text-white p-1 rounded transition-colors">
                        <X size={20} />
                    </button>
                </div>
            </div>

            <div className="flex-1 overflow-y-auto p-6 bg-slate-900">
                <div className="grid grid-cols-2 gap-4 mb-6">
                    <div>
                        <label className="block text-xs font-medium text-slate-300 mb-1">Blueprint Name</label>
                        <input value={name} onChange={e => setName(e.target.value)} className="w-full bg-slate-800 border border-slate-700 rounded-lg p-2 text-sm text-white focus:outline-none focus:border-blue-500 font-medium" />
                    </div>
                    <div>
                        <label className="block text-xs font-medium text-slate-300 mb-1">Version (SemVer)</label>
                        <input value={version} onChange={e => setVersion(e.target.value)} className="w-full bg-slate-800 border border-slate-700 rounded-lg p-2 text-sm text-white font-mono focus:outline-none focus:border-blue-500" />
                    </div>
                </div>

                {jsonMode ? (
                    <textarea 
                        value={rawJson} 
                        onChange={e => setRawJson(e.target.value)} 
                        className="w-full h-full font-mono text-xs p-4 border border-slate-800 rounded-xl bg-slate-950 text-blue-300/90 min-h-[500px] leading-relaxed focus:outline-none focus:border-blue-500"
                    />
                ) : (
                    <div className="space-y-8">
                        {/* 1. Events */}
                        <section className="border border-indigo-100 rounded-lg overflow-hidden">
                            <div className="bg-indigo-50 px-4 py-2 border-b border-indigo-100">
                                <h3 className="font-semibold text-indigo-900">1. Events (Facts)</h3>
                                <p className="text-xs text-indigo-700">What facts can happen in this domain?</p>
                            </div>
                            <div className="p-4 space-y-2">
                                {events.map((evt, idx) => (
                                    <div key={idx} className="flex gap-2">
                                        <input value={evt.eventId} onChange={e => {
                                            const newEvts = [...events]; newEvts[idx].eventId = e.target.value; setEvents(newEvts);
                                        }} placeholder="Event ID (e.g. EVT-SUBMIT)" className="flex-1 border p-2 rounded text-sm" />
                                        <input value={evt.name} onChange={e => {
                                            const newEvts = [...events]; newEvts[idx].name = e.target.value; setEvents(newEvts);
                                        }} placeholder="Display Name" className="flex-1 border p-2 rounded text-sm" />
                                        <button onClick={() => setEvents(events.filter((_, i) => i !== idx))} className="text-red-500 hover:bg-red-50 p-1 rounded">×</button>
                                    </div>
                                ))}
                                <button onClick={() => setEvents([...events, {eventId: '', name: ''}])} className="text-indigo-600 text-sm font-medium hover:underline">+ Add Event</button>
                            </div>
                        </section>

                        {/* 2. State Machine */}
                        <section className="border border-amber-100 rounded-lg overflow-hidden">
                            <div className="bg-amber-50 px-4 py-2 border-b border-amber-100">
                                <h3 className="font-semibold text-amber-900">2. State Machine (The Law)</h3>
                                <p className="text-xs text-amber-700">Defines legal statuses and transitions. Cannot be bypassed.</p>
                            </div>
                            <div className="p-4 space-y-4">
                                <div>
                                    <label className="block text-xs font-medium text-gray-700 mb-1">States</label>
                                    <div className="flex flex-wrap gap-2 mb-2">
                                        {states.map((st, idx) => (
                                            <span key={idx} className="bg-white border px-2 py-1 rounded flex items-center gap-1 text-sm">
                                                {st}
                                                <button onClick={() => setStates(states.filter(s => s !== st))} className="text-red-500 text-xs ml-1">×</button>
                                            </span>
                                        ))}
                                        <input 
                                            placeholder="+ Add State (Enter)" 
                                            className="border p-1 rounded text-sm min-w-[120px]" 
                                            onKeyDown={e => {
                                                if(e.key === 'Enter') {
                                                    setStates([...states, e.currentTarget.value]);
                                                    e.currentTarget.value = '';
                                                }
                                            }}
                                        />
                                    </div>
                                </div>
                                
                                <div>
                                    <label className="block text-xs font-medium text-gray-700 mb-1">Initial State</label>
                                    <select 
                                        value={initialState} 
                                        onChange={e => setInitialState(e.target.value)}
                                        className="w-full border p-2 rounded text-sm"
                                    >
                                        {states.map(s => <option key={s} value={s}>{s}</option>)}
                                    </select>
                                </div>

                                <div>
                                    <label className="block text-xs font-medium text-gray-700 mb-1">Legal Transitions</label>
                                    <div className="space-y-2">
                                        {transitions.map((t, idx) => (
                                            <div key={idx} className="flex gap-2 items-center bg-gray-50 p-2 rounded">
                                                <select value={t.from} onChange={e => {
                                                    const newTr = [...transitions]; newTr[idx].from = e.target.value; setTransitions(newTr);
                                                }} className="border p-1 rounded text-sm flex-1">
                                                    {states.map(s => <option key={s} value={s}>{s}</option>)}
                                                </select>
                                                <span className="text-gray-400 text-xs">+</span>
                                                <select value={t.evt} onChange={e => {
                                                    const newTr = [...transitions]; newTr[idx].evt = e.target.value; setTransitions(newTr);
                                                }} className="border p-1 rounded text-sm flex-1">
                                                    <option value="">(Event)</option>
                                                    {events.map(e => <option key={e.eventId} value={e.eventId}>{e.name}</option>)}
                                                </select>
                                                <span className="text-gray-400 text-xs">→</span>
                                                <select value={t.to} onChange={e => {
                                                    const newTr = [...transitions]; newTr[idx].to = e.target.value; setTransitions(newTr);
                                                }} className="border p-1 rounded text-sm flex-1">
                                                    {states.map(s => <option key={s} value={s}>{s}</option>)}
                                                </select>
                                                <button onClick={() => setTransitions(transitions.filter((_, i) => i !== idx))} className="text-red-500">×</button>
                                            </div>
                                        ))}
                                        <button onClick={() => setTransitions([...transitions, {from: states[0], to: states[0], evt: ''}])} className="text-amber-600 text-sm font-medium hover:underline">+ Add Transition</button>
                                    </div>
                                </div>
                            </div>
                        </section>

                        {/* 3. Workflow */}
                        <section className="border border-green-100 rounded-lg overflow-hidden">
                            <div className="bg-green-50 px-4 py-2 border-b border-green-100">
                                <h3 className="font-semibold text-green-900">3. Workflow (The Work)</h3>
                                <p className="text-xs text-green-700">Procedural steps. Steps ≠ States.</p>
                            </div>
                            <div className="p-4 space-y-4">
                                <div>
                                    <label className="block text-xs font-medium text-gray-700 mb-1">Start Step ID</label>
                                    <input 
                                        value={startStepId} 
                                        onChange={e => setStartStepId(e.target.value)} 
                                        className="w-full border p-2 rounded text-sm" 
                                        placeholder="e.g. Start"
                                    />
                                </div>

                                <div className="space-y-4">
                                    {steps.map((step, idx) => (
                                        <div key={idx} className="border bg-white p-4 rounded shadow-sm relative group">
                                            <button onClick={() => setSteps(steps.filter((_, i) => i !== idx))} className="absolute top-2 right-2 text-gray-300 hover:text-red-500 opacity-0 group-hover:opacity-100 transition-opacity">
                                                <X size={16} />
                                            </button>
                                            
                                            <div className="grid grid-cols-2 gap-4 mb-3">
                                                <div>
                                                    <label className="block text-xs text-gray-500">Step ID</label>
                                                    <input 
                                                        value={step.stepId} 
                                                        onChange={e => {
                                                            const newSteps = [...steps]; newSteps[idx].stepId = e.target.value; setSteps(newSteps);
                                                        }}
                                                        className="w-full border p-2 rounded text-sm font-medium" 
                                                        placeholder="Step ID"
                                                    />
                                                </div>
                                                <div>
                                                    <label className="block text-xs text-gray-500">Type</label>
                                                    <select 
                                                        value={step.stepType} 
                                                        onChange={e => {
                                                            const newSteps = [...steps]; newSteps[idx].stepType = e.target.value; setSteps(newSteps);
                                                        }}
                                                        className="w-full border p-2 rounded text-sm"
                                                    >
                                                        <option value="Command">Command</option>
                                                        <option value="HumanTask">HumanTask</option>
                                                        <option value="Event">Event</option>
                                                        <option value="Decision">Decision</option>
                                                    </select>
                                                </div>
                                            </div>

                                            <div className="mb-3">
                                                <label className="block text-xs text-gray-500 mb-1">Next Steps (Routes)</label>
                                                {step.nextSteps.map((ns, nsIdx) => (
                                                    <div key={nsIdx} className="flex gap-2 mb-2 items-center">
                                                        <select 
                                                            value={ns.outcome} 
                                                            onChange={e => {
                                                                const newSteps = [...steps]; 
                                                                newSteps[idx].nextSteps[nsIdx].outcome = e.target.value; 
                                                                setSteps(newSteps);
                                                            }}
                                                            className="flex-1 border p-1 rounded text-xs"
                                                        >
                                                            <option value="">(Select Outcome)</option>
                                                            <option value="Default">Default</option>
                                                            {events.map(e => <option key={e.eventId} value={e.eventId}>{e.name}</option>)}
                                                        </select>
                                                        <span className="text-gray-400 text-xs">→</span>
                                                        <input 
                                                            value={ns.target} 
                                                            onChange={e => {
                                                                const newSteps = [...steps]; 
                                                                newSteps[idx].nextSteps[nsIdx].target = e.target.value; 
                                                                setSteps(newSteps);
                                                            }}
                                                            placeholder="Target Step ID"
                                                            className="flex-1 border p-1 rounded text-xs"
                                                        />
                                                        <button 
                                                            onClick={() => {
                                                                const newSteps = [...steps]; 
                                                                newSteps[idx].nextSteps = newSteps[idx].nextSteps.filter((_, i) => i !== nsIdx);
                                                                setSteps(newSteps);
                                                            }}
                                                            className="text-red-400 hover:text-red-600"
                                                        >×</button>
                                                    </div>
                                                ))}
                                                <button 
                                                    onClick={() => {
                                                        const newSteps = [...steps];
                                                        newSteps[idx].nextSteps.push({ outcome: '', target: '' });
                                                        setSteps(newSteps);
                                                    }}
                                                    className="text-xs text-green-600 hover:underline"
                                                >+ Add Route</button>
                                            </div>

                                            <div>
                                                <label className="block text-xs text-gray-500">Roles</label>
                                                <input 
                                                    value={step.roles}
                                                    onChange={e => {
                                                        const newSteps = [...steps]; newSteps[idx].roles = e.target.value; setSteps(newSteps);
                                                    }}
                                                    className="w-full border p-1 rounded text-sm"
                                                    placeholder="Required Roles (comma separated)"
                                                />
                                            </div>
                                        </div>
                                    ))}
                                    <button 
                                        onClick={() => setSteps([...steps, { stepId: '', stepType: 'Command', nextSteps: [], roles: '' }])}
                                        className="w-full py-2 border-2 border-dashed border-gray-300 rounded text-gray-500 hover:border-blue-300 hover:text-blue-500 transition-colors"
                                    >
                                        + Add Workflow Step
                                    </button>
                                </div>
                            </div>
                        </section>
                    </div>
                )}
            </div>

            <div className="p-4 border-t border-slate-800 bg-slate-950/70 flex justify-between items-center">
                <span className="text-xs text-slate-400 italic">Authoritative validation is run automatically on save.</span>
                <button onClick={handleSave} className="px-6 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-xl flex items-center gap-2 font-semibold text-xs shadow-lg transition-all">
                    <Save size={16} />
                    Save Draft
                </button>
            </div>
        </div>

        {/* Right: Validation Panel (Sticky) */}
        <div className="w-[350px] bg-slate-950/80 border-l border-slate-800 flex flex-col">
            <div className="p-4 border-b border-slate-800 bg-slate-950/90">
                <h3 className="font-bold text-white text-sm flex items-center gap-2">
                    {validation ? (
                        validation.isValid ? <CheckCircle className="text-emerald-400" size={18} /> : <AlertTriangle className="text-rose-400" size={18} />
                    ) : (
                        <Info className="text-slate-400" size={18} />
                    )}
                    Validation Report
                </h3>
            </div>
            
            <div className="flex-1 overflow-y-auto p-4">
                {!validation ? (
                    <div className="text-center text-slate-500 mt-10 space-y-2">
                        <p className="text-xs">Save the draft to run full validation.</p>
                        <p className="text-[11px] text-slate-600">Validation verifies Schema, Graph Completeness, and Governance Rules.</p>
                    </div>
                ) : validation.isValid ? (
                    <div className="text-center text-emerald-400 mt-10 space-y-1">
                        <p className="font-semibold text-sm">No issues found.</p>
                        <p className="text-xs text-slate-400">Blueprint is valid and ready to be published to engine.</p>
                    </div>
                ) : (
                    <div className="space-y-3">
                        {validation.errors.map((err, idx) => (
                            <div key={idx} className="bg-slate-900 p-3 rounded-xl border border-rose-500/30 shadow-sm border-l-4 border-l-rose-500">
                                <div className="flex justify-between items-start mb-1">
                                    <span className="text-[10px] font-bold text-rose-400 font-mono tracking-wider">{err.code}</span>
                                    <span className="text-[10px] text-slate-500">{err.category}</span>
                                </div>
                                <p className="text-xs font-medium text-slate-200 mb-1">{err.message}</p>
                                {err.element && (
                                    <div className="text-[10px] text-slate-400 mt-1.5 bg-slate-950 p-1.5 rounded font-mono">
                                        Location: <span className="text-blue-300">{err.element}</span>
                                    </div>
                                )}
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </div>

      </div>
    </div>
  );
};
