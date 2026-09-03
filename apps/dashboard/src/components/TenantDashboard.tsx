import React, { useState, useEffect } from 'react';
import { AuthSession, WorkflowClass, WorkflowInstance, ValidationResult, CreateDraftRequest } from '../types';
import { api } from '../api/client';
import { WorkflowTable } from './WorkflowTable';
import { WorkflowInstanceTable } from './WorkflowInstanceTable';
import { EventAuditViewer } from './EventAuditViewer';
import { TenantApiKeyManager } from './TenantApiKeyManager';
import { DetailView } from './DetailView';
import { EditorView } from './EditorView';
import { 
  Building2, Plus, Play, RefreshCw, Key, Activity, FileText, 
  Cpu, Copy, Check
} from 'lucide-react';

interface Props {
  session: AuthSession;
  onSwitchWorkspace: () => void;
}

export const TenantDashboard: React.FC<Props> = ({ session, onSwitchWorkspace }) => {
  const [activeTab, setActiveTab] = useState<'Instances' | 'Blueprints' | 'Events' | 'Keys' | 'Simulator'>('Instances');
  const [blueprintSubTab, setBlueprintSubTab] = useState<'All' | 'Published' | 'Drafts' | 'Shared'>('All');
  
  const [blueprints, setBlueprints] = useState<WorkflowClass[]>([]);
  const [instances, setInstances] = useState<WorkflowInstance[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copiedTenantId, setCopiedTenantId] = useState(false);

  // Modals
  const [selectedBlueprint, setSelectedBlueprint] = useState<WorkflowClass | null>(null);
  const [editorBlueprint, setEditorBlueprint] = useState<WorkflowClass | null>(null);
  const [isCreatingBlueprint, setIsCreatingBlueprint] = useState(false);
  const [validationResult, setValidationResult] = useState<ValidationResult | null>(null);

  // Start Instance Modal
  const [showStartModal, setShowStartModal] = useState(false);
  const [startWorkflowName, setStartWorkflowName] = useState('ExpenseApprovalV2');
  const [startingInstance, setStartingInstance] = useState(false);

  // Simulator State
  const [simStep, setSimStep] = useState<string>('Draft');
  const [simState, setSimState] = useState<string>('Draft');
  const [simLogs, setSimLogs] = useState<string[]>([
    `> FlowOS Dual-Kernel Engine Initialized for Tenant: ${session.tenantName}`,
    `> Workspace Isolated UUID: ${session.tenantId}`,
    '> Ready to simulate state machine transitions.'
  ]);

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      if (activeTab === 'Instances') {
        const instList = await api.listInstances('Tenant');
        setInstances(instList);
      } else if (activeTab === 'Blueprints') {
        const bpList = await api.list(undefined, undefined, 'Tenant');
        setBlueprints(bpList);
      }
    } catch (err: any) {
      setError(err.message || 'Failed to load workspace data');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [activeTab, session.tenantId]);

  const handleCopyTenantId = () => {
    navigator.clipboard.writeText(session.tenantId);
    setCopiedTenantId(true);
    setTimeout(() => setCopiedTenantId(false), 2000);
  };

  const handleStartInstance = async () => {
    setStartingInstance(true);
    try {
      const res = await api.startInstance(startWorkflowName, undefined, undefined, 'Tenant');
      const instanceId = res.WorkflowInstanceId || res.workflowInstanceId || res.Id || res.id;
      alert(`Workflow Instance Started Successfully!\nInstance ID: ${instanceId}`);
      setShowStartModal(false);
      setActiveTab('Instances');
      await loadData();
    } catch (err: any) {
      alert(`Failed to start instance: ${err.message}`);
    } finally {
      setStartingInstance(false);
    }
  };

  // Blueprint Handlers
  const handleViewBlueprint = async (id: string) => {
    try {
      const item = await api.get(id, 'Tenant');
      setSelectedBlueprint(item);
      setValidationResult(null);
    } catch (err: any) {
      setError(err.message);
    }
  };

  const handleEditBlueprint = async (id: string) => {
    try {
      const item = await api.get(id, 'Tenant');
      setEditorBlueprint(item);
    } catch (err: any) {
      setError(err.message);
    }
  };

  const handleSaveDraft = async (req: CreateDraftRequest) => {
    try {
      let savedItem: WorkflowClass;
      if (editorBlueprint) {
        savedItem = await api.updateDraft(editorBlueprint.id, req, 'Tenant');
      } else {
        savedItem = await api.createDraft(req, 'Tenant');
      }
      const result = await api.validate(savedItem.id, 'Tenant');
      setValidationResult(result);
      setEditorBlueprint(null);
      setIsCreatingBlueprint(false);
      await loadData();
    } catch (err: any) {
      alert(`Failed to save draft: ${err.message}`);
    }
  };

  // Simulator Triggers
  const handleSimulateEvent = (eventId: string, targetState: string, targetStep: string) => {
    if (simState === 'Completed' || simStep === 'END') {
      alert('Instance is already Completed. Click Reset.');
      return;
    }
    setSimState(targetState);
    setSimStep(targetStep);
    setSimLogs(prev => [
      ...prev,
      `> [${new Date().toLocaleTimeString()}] Published Event '${eventId}' -> State Transitioned to '${targetState}' | Step: '${targetStep}'`
    ]);
  };

  const handleResetSimulator = () => {
    setSimStep('Draft');
    setSimState('Draft');
    setSimLogs([
      `> FlowOS Engine reset for Tenant: ${session.tenantName}`,
      '> Instance restarted: Initial State = Draft'
    ]);
  };

  return (
    <div className="space-y-6">
      
      {/* Workspace Banner */}
      <div className="bg-gradient-to-r from-blue-900/40 via-slate-900 to-slate-900 border border-blue-500/30 p-6 rounded-3xl shadow-xl relative overflow-hidden">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-6 relative z-10">
          <div>
            <div className="flex items-center gap-2 mb-2">
              <span className="px-2.5 py-1 rounded-full text-xs font-bold bg-blue-500/20 text-blue-300 border border-blue-500/30 flex items-center gap-1.5">
                <Building2 size={13} /> Tenant Isolated Workspace
              </span>
              <span className="text-xs text-slate-500">•</span>
              <span className="text-xs text-slate-400 font-mono flex items-center gap-1">
                UUID: <strong>{session.tenantId.substring(0, 13)}...</strong>
                <button 
                  onClick={handleCopyTenantId}
                  className="hover:text-white text-slate-500"
                  title="Copy Tenant UUID"
                >
                  {copiedTenantId ? <Check size={12} className="text-emerald-400" /> : <Copy size={12} />}
                </button>
                <button
                  onClick={onSwitchWorkspace}
                  className="ml-2 text-[10px] text-blue-400 hover:underline"
                >
                  Switch Account
                </button>
              </span>
            </div>

            <h1 className="text-2xl md:text-3xl font-extrabold text-white">
              {session.tenantName}
            </h1>
            <p className="text-xs text-slate-400 mt-1 max-w-2xl">
              Operating environment with strict zero-trust boundary. Workflows, live state machines, and event streams are private to this tenant.
            </p>
          </div>

          {/* Quick Actions */}
          <div className="flex flex-wrap items-center gap-2.5">
            <button
              onClick={() => setShowStartModal(true)}
              className="px-4 py-2.5 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold rounded-xl text-xs flex items-center gap-2 shadow-lg shadow-emerald-500/25 transition-all"
            >
              <Play size={14} />
              <span>Launch Instance</span>
            </button>
            <button
              onClick={() => setIsCreatingBlueprint(true)}
              className="px-4 py-2.5 bg-blue-600 hover:bg-blue-500 text-white font-semibold rounded-xl text-xs flex items-center gap-2 shadow-lg shadow-blue-500/25 transition-all"
            >
              <Plus size={14} />
              <span>New Blueprint</span>
            </button>
            <button
              onClick={() => setActiveTab('Keys')}
              className="px-3.5 py-2.5 bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-300 rounded-xl text-xs font-semibold flex items-center gap-1.5 transition-colors"
            >
              <Key size={14} className="text-amber-400" />
              <span>API Keys</span>
            </button>
            <button
              onClick={loadData}
              className="p-2.5 bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-300 rounded-xl transition-colors"
              title="Refresh workspace"
            >
              <RefreshCw size={14} className={loading ? 'animate-spin text-blue-400' : ''} />
            </button>
          </div>
        </div>

        {/* 4 Stat Counters */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mt-6 pt-6 border-t border-slate-800/80">
          <div className="bg-slate-900/60 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400">Running Instances</div>
            <div className="text-lg font-bold text-white mt-0.5">{instances.length}</div>
          </div>
          <div className="bg-slate-900/60 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400">My Blueprints</div>
            <div className="text-lg font-bold text-blue-400 mt-0.5">{blueprints.length}</div>
          </div>
          <div className="bg-slate-900/60 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400">Tenant Authority</div>
            <div className="text-lg font-bold text-emerald-400 mt-0.5">Enforced</div>
          </div>
          <div className="bg-slate-900/60 p-3 rounded-xl border border-slate-800">
            <div className="text-[11px] text-slate-400">Kernel Role</div>
            <div className="text-lg font-bold text-slate-200 mt-0.5">Tenant</div>
          </div>
        </div>
      </div>

      {error && (
        <div className="p-4 bg-rose-900/30 border border-rose-700 rounded-xl text-xs text-rose-300 flex items-center gap-2">
          <span>{error}</span>
        </div>
      )}

      {/* Tenant Navigation Tabs */}
      <div className="bg-slate-800 border border-slate-700 rounded-2xl overflow-hidden shadow-xl">
        <div className="border-b border-slate-700 bg-slate-850">
          <nav className="flex divide-x divide-slate-700 text-xs font-semibold">
            <button
              onClick={() => setActiveTab('Instances')}
              className={`flex-1 py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 ${
                activeTab === 'Instances' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Activity size={15} />
              <span>⚡ My Live Instances ({instances.length})</span>
            </button>
            <button
              onClick={() => setActiveTab('Blueprints')}
              className={`flex-1 py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 ${
                activeTab === 'Blueprints' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <FileText size={15} />
              <span>📑 My Workflow Blueprints ({blueprints.length})</span>
            </button>
            <button
              onClick={() => setActiveTab('Events')}
              className={`flex-1 py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 ${
                activeTab === 'Events' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Activity size={15} />
              <span>📡 Event Stream & Payloads</span>
            </button>
            <button
              onClick={() => setActiveTab('Keys')}
              className={`flex-1 py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 ${
                activeTab === 'Keys' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Key size={15} />
              <span>🔑 Applications & API Keys</span>
            </button>
            <button
              onClick={() => setActiveTab('Simulator')}
              className={`flex-1 py-3.5 px-4 text-center transition-all flex items-center justify-center gap-2 ${
                activeTab === 'Simulator' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white hover:bg-slate-750'
              }`}
            >
              <Cpu size={15} />
              <span>🧪 State Machine Simulator</span>
            </button>
          </nav>
        </div>

        {/* Tab Contents */}
        <div className="p-6">
          {activeTab === 'Instances' && (
            <div className="space-y-4">
              <div className="flex justify-between items-center text-xs text-slate-400">
                <span>Showing workflow executions owned by <strong>{session.tenantName}</strong></span>
                <button
                  onClick={() => setShowStartModal(true)}
                  className="px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg text-xs font-semibold flex items-center gap-1.5"
                >
                  <Play size={13} /> Launch Workflow
                </button>
              </div>
              <WorkflowInstanceTable items={instances} />
            </div>
          )}

          {activeTab === 'Blueprints' && (
            <div className="space-y-4">
              {/* Subtabs for Blueprints */}
              <div className="flex items-center justify-between">
                <div className="flex bg-slate-900 rounded-xl p-1 border border-slate-700 text-xs">
                  {(['All', 'Published', 'Drafts', 'Shared'] as const).map(sub => (
                    <button
                      key={sub}
                      onClick={() => setBlueprintSubTab(sub)}
                      className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${
                        blueprintSubTab === sub ? 'bg-blue-600 text-white shadow-sm' : 'text-slate-400 hover:text-slate-200'
                      }`}
                    >
                      {sub === 'Shared' ? 'Under Platform Review' : sub}
                    </button>
                  ))}
                </div>

                <button
                  onClick={() => setIsCreatingBlueprint(true)}
                  className="px-3.5 py-1.5 bg-blue-600 hover:bg-blue-500 text-white rounded-xl text-xs font-semibold flex items-center gap-1.5 shadow-sm"
                >
                  <Plus size={14} /> New Workflow Blueprint
                </button>
              </div>

              <WorkflowTable
                items={blueprints}
                currentTab={blueprintSubTab}
                isAdmin={false}
                onView={handleViewBlueprint}
                onEdit={handleEditBlueprint}
                onDelete={async (id) => {
                  if (confirm('Delete this draft?')) {
                    await api.delete(id, 'Tenant');
                    await loadData();
                  }
                }}
                onPublish={async (id) => {
                  await api.publish(id, 'Tenant');
                  setBlueprintSubTab('Published');
                  await loadData();
                }}
                onSubmit={async (id) => {
                  await api.submit(id, 'Tenant');
                  setBlueprintSubTab('Shared');
                  await loadData();
                }}
                onWithdraw={async (id) => {
                  await api.withdraw(id, 'Tenant');
                  setBlueprintSubTab('Drafts');
                  await loadData();
                }}
                onDeprecate={async (id) => {
                  await api.deprecate(id, 'Tenant');
                  await loadData();
                }}
                onAbandon={async (id) => {
                  await api.abandon(id, 'Tenant');
                  await loadData();
                }}
                onNewVersion={async (id) => {
                  const nv = await api.newVersion(id, 'Tenant');
                  setEditorBlueprint(nv);
                  await loadData();
                }}
              />
            </div>
          )}

          {activeTab === 'Events' && (
            <EventAuditViewer role="Tenant" onInspectWorkflow={() => setActiveTab('Instances')} />
          )}

          {activeTab === 'Keys' && (
            <TenantApiKeyManager tenantId={session.tenantId} tenantName={session.tenantName} />
          )}

          {activeTab === 'Simulator' && (
            <div className="bg-slate-900 border border-slate-700/80 rounded-2xl p-6 shadow-xl grid grid-cols-1 lg:grid-cols-3 gap-8">
              {/* Controls */}
              <div className="space-y-4">
                <div className="bg-slate-950 p-4 rounded-xl border border-slate-800 space-y-2 text-xs">
                  <div className="flex justify-between">
                    <span className="text-slate-400">Workflow Step:</span>
                    <span className="font-mono font-bold text-blue-400">{simStep}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Legal State:</span>
                    <span className="font-mono font-bold text-emerald-400">{simState}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Status:</span>
                    <span className="px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-300 font-semibold">
                      {simState === 'Completed' ? 'Completed' : 'Running'}
                    </span>
                  </div>
                </div>

                <div className="text-xs font-bold text-slate-300 uppercase tracking-wider">
                  Available Events:
                </div>

                <div className="space-y-2">
                  <button
                    onClick={() => handleSimulateEvent('EVT-SUBMIT', 'Submitted', 'ManagerApproval')}
                    disabled={simStep !== 'Draft'}
                    className="w-full py-2.5 px-3 bg-blue-600 hover:bg-blue-500 disabled:opacity-40 text-white rounded-xl text-xs font-semibold flex items-center justify-between"
                  >
                    <span>Publish EVT-SUBMIT</span>
                    <span className="font-mono text-[10px]">Draft → Submitted</span>
                  </button>

                  <button
                    onClick={() => handleSimulateEvent('EVT-APPROVE-MGR', 'Approved', 'DirectorApproval')}
                    disabled={simStep !== 'ManagerApproval'}
                    className="w-full py-2.5 px-3 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-40 text-white rounded-xl text-xs font-semibold flex items-center justify-between"
                  >
                    <span>Publish EVT-APPROVE-MGR</span>
                    <span className="font-mono text-[10px]">Submitted → Approved</span>
                  </button>

                  <button
                    onClick={() => handleSimulateEvent('EVT-APPROVE-DIR', 'Completed', 'END')}
                    disabled={simStep !== 'DirectorApproval'}
                    className="w-full py-2.5 px-3 bg-purple-600 hover:bg-purple-500 disabled:opacity-40 text-white rounded-xl text-xs font-semibold flex items-center justify-between"
                  >
                    <span>Publish EVT-APPROVE-DIR</span>
                    <span className="font-mono text-[10px]">Director → END</span>
                  </button>

                  <button
                    onClick={() => handleSimulateEvent('EVT-TIMEOUT', 'Escalated', 'ManualReview')}
                    disabled={simState === 'Completed'}
                    className="w-full py-2.5 px-3 bg-rose-600/80 hover:bg-rose-600 disabled:opacity-40 text-white rounded-xl text-xs font-semibold flex items-center justify-between"
                  >
                    <span>Simulate SLA Timeout</span>
                    <span className="font-mono text-[10px]">Trigger Escalation</span>
                  </button>
                </div>

                <button
                  onClick={handleResetSimulator}
                  className="w-full py-2 border border-slate-700 hover:bg-slate-800 text-slate-300 rounded-xl text-xs font-semibold"
                >
                  Reset Simulator
                </button>
              </div>

              {/* Console Logs */}
              <div className="lg:col-span-2 flex flex-col h-80 bg-slate-950 rounded-xl border border-slate-800 p-4 font-mono text-xs overflow-hidden">
                <div className="flex items-center justify-between pb-2 border-b border-slate-800 text-slate-400">
                  <span className="flex items-center gap-1.5 text-[11px]">
                    <Activity size={12} className="text-emerald-400" />
                    Dual-Kernel Telemetry Console
                  </span>
                  <span className="text-[10px] text-slate-600">State Machine Law</span>
                </div>
                <div className="flex-1 overflow-y-auto space-y-1.5 pt-2 text-slate-300 text-[11px]">
                  {simLogs.map((log, i) => (
                    <div key={i} className="leading-relaxed font-mono">
                      {log.includes('Denied') ? (
                        <span className="text-rose-400 font-bold">{log}</span>
                      ) : log.includes('Transitioned') ? (
                        <span className="text-emerald-400 font-bold">{log}</span>
                      ) : log.includes('SLA') ? (
                        <span className="text-amber-400 font-bold">{log}</span>
                      ) : (
                        log
                      )}
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Start Instance Modal */}
      {showStartModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-md w-full p-6 shadow-2xl space-y-4">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <Play className="text-emerald-400" size={18} />
                Launch Workflow Instance
              </h3>
              <button onClick={() => setShowStartModal(false)} className="text-slate-400 hover:text-white">
                &times;
              </button>
            </div>

            <div className="space-y-3 text-xs">
              <div>
                <label className="text-slate-300 font-medium block mb-1">Published Workflow Name</label>
                <input
                  type="text"
                  value={startWorkflowName}
                  onChange={e => setStartWorkflowName(e.target.value)}
                  placeholder="e.g. ExpenseApprovalV2"
                  className="w-full bg-slate-950 border border-slate-700 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-blue-500"
                />
              </div>

              <div className="p-3 bg-slate-950 border border-slate-800 rounded-xl text-slate-400 text-[11px]">
                Workflow instance will be created and bound to tenant <strong>{session.tenantName}</strong> ({session.tenantId}).
              </div>

              <div className="pt-2 flex justify-end gap-2 border-t border-slate-800">
                <button
                  onClick={() => setShowStartModal(false)}
                  className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl font-semibold"
                >
                  Cancel
                </button>
                <button
                  onClick={handleStartInstance}
                  disabled={startingInstance}
                  className="px-4 py-2 bg-emerald-600 hover:bg-emerald-500 text-white rounded-xl font-semibold flex items-center gap-1.5"
                >
                  {startingInstance ? 'Starting...' : 'Start Workflow'}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Blueprint Detail & Editor Modals */}
      {selectedBlueprint && (
        <DetailView 
          item={selectedBlueprint} 
          validation={validationResult}
          onClose={() => setSelectedBlueprint(null)}
          onValidate={async () => {
            if (selectedBlueprint) {
              const res = await api.validate(selectedBlueprint.id, 'Tenant');
              setValidationResult(res);
            }
          }}
        />
      )}

      {(isCreatingBlueprint || editorBlueprint) && (
        <EditorView 
          item={editorBlueprint || undefined}
          validation={validationResult}
          onClose={() => { setEditorBlueprint(null); setIsCreatingBlueprint(false); setValidationResult(null); }}
          onSave={handleSaveDraft}
        />
      )}

    </div>
  );
};
